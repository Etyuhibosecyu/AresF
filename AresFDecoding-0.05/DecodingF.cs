global using AresGlobalMethods;
global using Corlib.NStar;
global using System;
global using System.Runtime.InteropServices;
global using System.Text;
global using System.Threading;
global using System.Threading.Tasks;
global using UnsafeFunctions;
global using G = System.Collections.Generic;
global using static AresFLib.Global;
global using static Corlib.NStar.Extents;
global using static System.Math;
global using static UnsafeFunctions.Global;
global using String = Corlib.NStar.String;
using System.Text.RegularExpressions;
using Mpir.NET;

namespace AresFLib;

public enum UsedMethodsF
{
	None = 0,
	CS1 = 1,
	LZ1 = 1 << 1,
	HF1 = 1 << 2,
	//Dev1 = 1 << 3,
	//Dev1_2 = 1 << 4,
	CS2 = 1 << 5,
	AHF2 = 1 << 6,
	//Dev2 = 1 << 7,
	CS3 = 1 << 8,
	//Dev3 = 1 << 9,
	CS4 = 1 << 10,
	//Dev4 = 1 << 11,
	CS5 = 1 << 12,
}

public static class Global
{
	public const byte ProgramVersion = 1;
	public const int WordsListActualParts = 3;
	public static int BWTBlockSize { get; set; } = 50000;
#pragma warning disable CS0652 // Сравнение с константой интеграции бесполезно: константа находится за пределами диапазона типа
	public static int BWTBlockExtraSize => BWTBlockSize <= 0x8000 ? 2 : BWTBlockSize <= 0x800000 ? 3 : BWTBlockSize <= 0x80000000 ? 4 : BWTBlockSize <= 0x8000000000 ? 5 : BWTBlockSize <= 0x800000000000 ? 6 : BWTBlockSize <= 0x80000000000000 ? 7 : 8;
#pragma warning restore CS0652 // Сравнение с константой интеграции бесполезно: константа находится за пределами диапазона типа
	public static int FragmentLength { get; set; } = 16000000;
	public static int PreservedFragmentLength { get; set; } = FragmentLength;
	public static UsedMethodsF PresentMethodsF { get; set; } = UsedMethodsF.CS1 | UsedMethodsF.HF1 | UsedMethodsF.LZ1;
}

public class DecodingF
{
	protected ArithmeticDecoder ar = default!;
	protected int misc, hf, rle, lz, bwt, n;
	protected int repeatsCount = 1;

	public virtual byte[] Decode(byte[] compressedFile, byte encodingVersion)
	{
		if (compressedFile.Length <= 2)
			return [];
		if (encodingVersion == 0)
			return compressedFile;
		else if (encodingVersion < ProgramVersion)
			return encodingVersion switch
			{
				_ => throw new DecoderFallbackException(),
			};
		if (ProcessMethod(compressedFile) is byte[] bytes)
			return bytes;
		var byteList = ProcessMisc(compressedFile);
		Current[0] += ProgressBarStep;
		if (rle == 12)
			byteList = new RLEDec(byteList).DecodeRLE3();
		Current[0] += ProgressBarStep;
		if (rle == 6)
			byteList = new RLEDec(byteList).Decode();
		return [.. byteList.Repeat(repeatsCount)];
	}

	protected virtual byte[]? ProcessMethod(byte[] compressedFile)
	{
		int method = compressedFile[0];
		if (method == 0)
			return compressedFile[1..];
		else if (compressedFile.Length <= 2)
			throw new DecoderFallbackException();
		SplitMethod(method);
		if (method != 0 && compressedFile.Length <= 5)
			throw new DecoderFallbackException();
		return null;
	}

	protected virtual NList<byte> ProcessMisc(byte[] compressedFile) => misc switch
	{
		1 => ProcessMisc1(compressedFile),
		-1 => ProcessNonMisc(compressedFile),
		_ => throw new DecoderFallbackException(),
	};

	protected virtual NList<byte> ProcessMisc1(byte[] compressedFile)
	{
		ar = compressedFile[1..];
		return CreatePPM(ValuesInByte).Decode().PNConvert(x => (byte)x[0].Lower);
	}

	protected virtual NList<byte> ProcessNonMisc(byte[] compressedFile)
	{
		if (hf + lz + bwt != 0)
		{
			Current[0] = 0;
			CurrentMaximum[0] = ProgressBarStep * (bwt != 0 ? 4 : 3);
			ar = compressedFile[1..];
			return CreateDecoding2().Decode().PNConvert(x => (byte)x[0].Lower);
		}
		else
			return compressedFile.GetSlice(1).ToNList();
	}

	protected virtual void SplitMethod(int method)
	{
		misc = method >= 18 ? method % 18 % 6 : -1;
		hf = method % 18 % 6;
		rle = method % 18 / 6 * 6;
		lz = Min(method % 18 % 6, 4) % 2;
		bwt = method % 18 % 6 / 4;
	}

	public virtual void ProcessLZLength(LZData lzData, out uint length)
	{
		if (lzData.Length.R == 0)
			length = ar.ReadEqual(lzData.Length.Max + 1);
		else if (lzData.Length.R == 1)
		{
			length = ar.ReadEqual(lzData.Length.Threshold + 2);
			if (length == lzData.Length.Threshold + 1)
				length += ar.ReadEqual(lzData.Length.Max - lzData.Length.Threshold);
		}
		else
		{
			length = ar.ReadEqual(lzData.Length.Max - lzData.Length.Threshold + 2) + lzData.Length.Threshold;
			if (length == lzData.Length.Max + 1)
				length = ar.ReadEqual(lzData.Length.Threshold);
		}
	}

	public virtual void ProcessLZLength(LZData lzData, SumList lengthsSL, out int readIndex, out uint length)
	{
		readIndex = ar.ReadPart(lengthsSL);
		lengthsSL.Increase(readIndex);
		if (lzData.Length.R == 0)
			length = (uint)readIndex;
		else if (lzData.Length.R == 1)
		{
			length = (uint)readIndex;
			if (length == lzData.Length.Threshold + 1)
				length += ar.ReadEqual(lzData.Length.Max - lzData.Length.Threshold);
		}
		else
		{
			length = (uint)readIndex + lzData.Length.Threshold;
			if (length == lzData.Length.Max + 1)
				length = ar.ReadEqual(lzData.Length.Threshold);
		}
	}

	public virtual void ProcessLZDist(LZData lzData, int fullLength, out uint dist, uint length, out uint maxDist)
	{
		maxDist = Min(lzData.Dist.Max, (uint)(fullLength - length - 2));
		if (lzData.Dist.R == 0 || maxDist < lzData.Dist.Threshold)
			dist = ar.ReadEqual(maxDist + lzData.UseSpiralLengths + 1);
		else if (lzData.Dist.R == 1)
		{
			dist = ar.ReadEqual(lzData.Dist.Threshold + 2);
			if (dist == lzData.Dist.Threshold + 1)
				dist += ar.ReadEqual(maxDist - lzData.Dist.Threshold + lzData.UseSpiralLengths);
		}
		else
		{
			dist = ar.ReadEqual(maxDist - lzData.Dist.Threshold + 2) + lzData.Dist.Threshold;
			if (dist == maxDist + 1)
			{
				dist = ar.ReadEqual(lzData.Dist.Threshold + lzData.UseSpiralLengths);
				if (dist == lzData.Dist.Threshold)
					dist = maxDist + 1;
			}
		}
	}

	public virtual void ProcessLZDist(LZData lzData, SumList distsSL, int fullLength, out int readIndex, out uint dist, uint length, out uint maxDist)
	{
		maxDist = Min(lzData.Dist.Max, (uint)(fullLength - length - 2));
		readIndex = ar.ReadPart(distsSL);
		distsSL.Increase(readIndex);
		if (lzData.Dist.R == 0 || maxDist < lzData.Dist.Threshold)
			dist = (uint)readIndex;
		else if (lzData.Dist.R == 1)
		{
			dist = (uint)readIndex;
			if (dist == lzData.Dist.Threshold + 1)
				dist += ar.ReadEqual(maxDist - lzData.Dist.Threshold + lzData.UseSpiralLengths);
		}
		else
			dist = (uint)readIndex;
	}

	public virtual bool ProcessLZSpiralLength(LZData lzData, ref uint dist, out uint spiralLength, uint maxDist)
	{
		if (dist == maxDist + 1)
		{
			if (lzData.SpiralLength.R == 0)
				spiralLength = ar.ReadEqual(lzData.SpiralLength.Max + 1);
			else if (lzData.SpiralLength.R == 1)
			{
				spiralLength = ar.ReadEqual(lzData.SpiralLength.Threshold + 2);
				if (spiralLength == lzData.SpiralLength.Threshold + 1)
					spiralLength += ar.ReadEqual(lzData.SpiralLength.Max - lzData.SpiralLength.Threshold);
			}
			else
			{
				spiralLength = ar.ReadEqual(lzData.SpiralLength.Max - lzData.SpiralLength.Threshold + 2) + lzData.SpiralLength.Threshold;
				if (spiralLength == lzData.SpiralLength.Max + 1)
					spiralLength = ar.ReadEqual(lzData.SpiralLength.Threshold);
			}
			return true;
		}
		spiralLength = 0;
		return false;
	}

	public virtual void GetRepeatsCount()
	{
		var repeats = ar.ReadPart(new NList<uint>(2, 224, 225));
		repeatsCount = repeats == 0 ? 1 : (int)ar.ReadCount() + 2;
		if (repeatsCount > GetFragmentLength() >> 1)
			throw new DecoderFallbackException();
	}

	public virtual uint GetFragmentLength() => (uint)FragmentLength;

	public virtual List<ShortIntervalList> ReadCompressedList(HuffmanData huffmanData, int bwt, LZData lzData, int lz, int counter)
	{
		Status[0] = 0;
		StatusMaximum[0] = counter;
		List<ShortIntervalList> result = [];
		var startingArithmeticMap = lz == 0 ? huffmanData.ArithmeticMap : huffmanData.ArithmeticMap[..^1];
		var uniqueLists = huffmanData.UniqueList.ToList(x => new ShortIntervalList() { x });
		for (; counter > 0; counter--, Status[0]++)
		{
			var readIndex = ar.ReadPart(result.Length < 2 || bwt != 0 && (result.Length < 4 || (result.Length + 0) % (BWTBlockSize + 2) is 0 or 1) ? startingArithmeticMap : huffmanData.ArithmeticMap);
			if (!(lz != 0 && readIndex == huffmanData.ArithmeticMap.Length - 1))
			{
				result.Add(uniqueLists[readIndex]);
				continue;
			}
			ProcessLZLength(lzData, out var length);
			if (length > result.Length - 2)
				throw new DecoderFallbackException();
			ProcessLZDist(lzData, result.Length, out var dist, length, out var maxDist);
			if (ProcessLZSpiralLength(lzData, ref dist, out var spiralLength, maxDist))
				dist = 0;
			var start = (int)(result.Length - dist - length - 2);
			if (start < 0)
				throw new DecoderFallbackException();
			var fullLength = (int)((length + 2) * (spiralLength + 1));
			for (var i = fullLength; i > 0; i -= (int)length + 2)
			{
				var length2 = (int)Min(length + 2, i);
				result.AddRange(result.GetSlice(start, length2));
			}
		}
		return result;
	}

	protected virtual Decoding2F CreateDecoding2() => new(this, ar, hf, bwt, lz);

	protected virtual PPMDecF CreatePPM(uint @base) => new(this, ar, @base);

	public virtual List<ShortIntervalList> DecodeBWT(List<ShortIntervalList> input, NList<byte> skipped)
	{
		Status[0] = 0;
		StatusMaximum[0] = GetArrayLength(input.Length, BWTBlockSize + BWTBlockExtraSize);
		var bytes = input.Convert(x => (byte)x[0].Lower);
		NList<byte> bytes2 = [];
		for (var i = 0; i < bytes.Length;)
		{
			var zle = bytes[i] & ValuesInByte >> 1;
			bytes2.AddRange(bytes.GetSlice(i..(i += BWTBlockExtraSize)));
			bytes2.AddRange(zle != 0 ? DecodeZLE(bytes, ref i) : bytes.GetRange(i..Min(i += BWTBlockSize, bytes.Length)));
		}
		var hs = bytes2.Filter((x, index) => index % (BWTBlockSize + BWTBlockExtraSize) >= BWTBlockExtraSize).ToHashSet().Concat(skipped).Sort().ToHashSet();
		List<ShortIntervalList> result = new(bytes2.Length);
		for (var i = 0; i < bytes2.Length; i += BWTBlockSize, Status[0]++)
		{
			if (bytes2.Length - i <= BWTBlockExtraSize)
				throw new DecoderFallbackException();
			var length = Min(BWTBlockSize, bytes2.Length - i - BWTBlockExtraSize);
			bytes2[i] &= (ValuesInByte >> 1) - 1;
			var firstPermutation = (int)bytes2.GetSlice(i, BWTBlockExtraSize).Progression(0L, (x, y) => unchecked((x << BitsPerByte) + y));
			i += BWTBlockExtraSize;
			result.AddRange(DecodeBWT2(bytes2.GetRange(i, length), hs, firstPermutation));
		}
		return result;
	}

	protected virtual List<ShortIntervalList> DecodeBWT2(NList<byte> input, ListHashSet<byte> hs, int firstPermutation)
	{
		var mtfMemory = hs.ToArray();
		for (var i = 0; i < input.Length; i++)
		{
			var index = hs.IndexOf(input[i]);
			input[i] = mtfMemory[index];
			Array.Copy(mtfMemory, 0, mtfMemory, 1, index);
			mtfMemory[0] = input[i];
		}
		var sorted = input.ToArray((elem, index) => (elem, index)).NSort(x => x.elem);
		var convert = sorted.ToArray(x => x.index);
		var result = RedStarLinq.EmptyList<ShortIntervalList>(input.Length);
		var it = firstPermutation;
		for (var i = 0; i < input.Length; i++)
		{
			it = convert[it];
			result[i] = [new(input[it], ValuesInByte)];
		}
		return result;
	}

	public virtual NList<byte> DecodeZLE(Slice<byte> byteList, ref int i)
	{
		if (i >= byteList.Length)
			throw new DecoderFallbackException();
		byte zero = byteList[i++], zeroB = byteList[i++];
		NList<byte> result = [];
		String zeroCode = ['1'];
		int length;
		for (; i < byteList.Length && result.Length < BWTBlockSize;)
		{
			while (i < byteList.Length && result.Length < BWTBlockSize && byteList[i] != zero && byteList[i] != zeroB)
				result.Add(byteList[i++]);
			if (i >= byteList.Length || result.Length >= BWTBlockSize)
				break;
			zeroCode.Remove(1);
			length = 0;
			while (i < byteList.Length && result.Length + length < BWTBlockSize && (byteList[i] == zero || byteList[i] == zeroB))
			{
				zeroCode.Add(byteList[i++] == zero ? '0' : '1');
				length = (int)(new MpzT(zeroCode.ToString(), 2) - 1);
			}
			result.AddRange(RedStarLinq.NFill(zero, length));
		}
		return result;
	}
}
