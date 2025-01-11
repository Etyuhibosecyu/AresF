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
global using static AresGlobalMethods.DecodingExtents;
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
	AHF1 = 1 << 1,
	//Dev1 = 1 << 2,
	CS2 = 1 << 3,
	LZ2 = 1 << 4,
	HF2 = 1 << 5,
	//Dev2 = 1 << 6,
	//Dev2_2 = 1 << 7,
	CS3 = 1 << 8,
	//Dev3 = 1 << 9,
	CS4 = 1 << 10,
	//Dev4 = 1 << 11,
	CS5 = 1 << 12,
}

public static class Global
{
	public const byte ProgramVersion = 1, PPMThreshold = 24;
	public static UsedMethodsF PresentMethodsF { get; set; } = UsedMethodsF.CS1 | UsedMethodsF.AHF1;
}

public class DecodingF : IDisposable
{
	protected GlobalDecoding globalDecoding = default!;
	protected ArithmeticDecoder ar = default!;
	protected int misc, hf, rle, lz, bwt, n;

	public virtual void Dispose()
	{
		ar?.Dispose();
		GC.SuppressFinalize(this);
	}

	public virtual NList<byte> Decode(NList<byte> compressedFile, byte encodingVersion)
	{
		if (compressedFile.Length <= 2)
			return [];
		if (encodingVersion == 0)
			return [.. compressedFile];
		else if (encodingVersion < ProgramVersion)
			return encodingVersion switch
			{
				_ => throw new DecoderFallbackException(),
			};
		if (ProcessMethod(compressedFile) is NList<byte> bytes)
			return bytes;
		var byteList = ProcessMisc(compressedFile);
		Current[0] += ProgressBarStep;
		Current[0] += ProgressBarStep;
		switch (rle)
		{
			case 0:
			break;
			case 1:
			{
				var oldByteList = byteList;
				byteList = new RLEDec(byteList).Decode();
				oldByteList.Dispose();
				break;
			}
			case 2:
			{
				var oldByteList = byteList;
				byteList = new RLEDec(byteList).DecodeRLE3();
				oldByteList.Dispose();
				break;
			}
			case 3:
			{
				var oldByteList = byteList;
				byteList = new RLEDec(byteList).DecodeMixed();
				oldByteList.Dispose();
				break;
			}
		}
		return byteList;
	}

	protected virtual NList<byte>? ProcessMethod(NList<byte> compressedFile)
	{
		int method = compressedFile[0];
		if (method == 0)
			return compressedFile.ToNList()[1..];
		else if (compressedFile.Length <= 2)
			throw new DecoderFallbackException();
		SplitMethod(method);
		if (method != 0 && compressedFile.Length <= 5)
			throw new DecoderFallbackException();
		return null;
	}

	protected virtual NList<byte> ProcessMisc(NList<byte> compressedFile) => misc switch
	{
		1 => ProcessMisc1(compressedFile),
		-1 => ProcessNonMisc(compressedFile),
		_ => throw new DecoderFallbackException(),
	};

	protected virtual NList<byte> ProcessMisc1(NList<byte> compressedFile)
	{
		if (compressedFile.Length <= 5)
			throw new DecoderFallbackException();
		var blocksCount = compressedFile[1] + 1;
		List<NList<byte>> blocks = new(blocksCount);
		var index = 2;
		for (var i = 0; i < blocksCount - 1; i++)
		{
			if (i != 0 && compressedFile.Length < index + 3)
				throw new DecoderFallbackException();
			var blockLength = (compressedFile[index++] << BitsPerByte | compressedFile[index++]) << BitsPerByte | compressedFile[index++];
			if (compressedFile.Length < index + blockLength)
				throw new DecoderFallbackException();
			blocks.Add(compressedFile[index..(index += blockLength)]);
		}
		blocks.Add(compressedFile[index..]);
		Current[0] = 0;
		CurrentMaximum[0] = ProgressBarStep * (blocksCount + 2);
		List<NList<byte>> ConvertBlocks(Func<NList<byte>, int, NList<byte>> x) => blocksCount <= ProgressBarGroups ? blocks.PConvert(x) : blocks.ToList(x);
		return ConvertBlocks((block, index) =>
		{
			var globalDecoding = CreateGlobalDecoding(block);
			using var ppm = globalDecoding.CreatePPM(ValuesInByte, tn: blocksCount <= ProgressBarGroups ? index : 0);
			var result = ppm.Decode().PNConvert(x => (byte)x[0].Lower);
			Current[0] += ProgressBarStep;
			return result;
		}).ConvertAndJoin(x => x).ToNList();
	}

	protected virtual NList<byte> ProcessNonMisc(NList<byte> compressedFile)
	{
		if (hf + lz + bwt != 0)
		{
			Current[0] = 0;
			CurrentMaximum[0] = ProgressBarStep * (bwt != 0 ? 4 : 3);
			ar = compressedFile[1..];
			globalDecoding = CreateGlobalDecoding();
			using var dec = CreateDecoding2();
			return dec.Decode().PNConvert(x => (byte)x[0].Lower);
		}
		else
			return compressedFile.GetRange(1);
	}

	protected virtual void SplitMethod(int method)
	{
		misc = method >= PPMThreshold ? method % PPMThreshold % 6 : -1;
		hf = method % PPMThreshold % 6;
		rle = method % PPMThreshold / 6;
		lz = Min(method % PPMThreshold % 6, 4) % 2;
		bwt = method % PPMThreshold % 6 / 4;
	}

	public virtual NList<ShortIntervalList> ReadCompressedList(HuffmanData huffmanData, int bwt, LZData lzData, int lz, int counter)
	{
		var decoding = CreateGlobalDecoding();
		Status[0] = 0;
		StatusMaximum[0] = counter;
		NList<ShortIntervalList> result = [];
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
			decoding.ProcessLZLength(lzData, out var length);
			if (length > result.Length - 2)
				throw new DecoderFallbackException();
			decoding.ProcessLZDist(lzData, result.Length, out var dist, length, out var maxDist);
			decoding.ProcessLZSpiralLength(lzData, ref dist, out var spiralLength, maxDist);
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

	public virtual GlobalDecoding CreateGlobalDecoding(ArithmeticDecoder? ar = null) => new(ar ?? this.ar);

	protected virtual Decoding2F CreateDecoding2() => new(this, ar, hf, bwt, lz);

	public virtual NList<ShortIntervalList> DecodeBWT(NList<ShortIntervalList> input, NList<byte> skipped, int bwtBlockSize)
	{
		var bwtBlockExtraSize = bwtBlockSize <= 0x4000 ? 2 : bwtBlockSize <= 0x400000 ? 3 : bwtBlockSize <= 0x40000000 ? 4 : 5;
		Status[0] = 0;
		StatusMaximum[0] = GetArrayLength(input.Length, bwtBlockSize + bwtBlockExtraSize);
		var numericInput = input.Convert(x => (short)x[0].Lower);
		NList<byte> bytes2 = new(numericInput.Length * 3);
		for (var i = 0; i < numericInput.Length;)
		{
			var zle = numericInput[i] & ValuesInByte >> 1;
			bytes2.AddRange(numericInput.GetSlice(i..(i += bwtBlockExtraSize)).Convert(x => (byte)x));
			bytes2.AddRange(zle != 0 ? DecodeZLE(numericInput, ref i, bwtBlockSize) : numericInput.GetRange(i..Min(i += bwtBlockSize, numericInput.Length)).Convert(x => (byte)x));
		}
		var hs = bytes2.Filter((x, index) => index % (bwtBlockSize + bwtBlockExtraSize) >= bwtBlockExtraSize).ToHashSet().Concat(skipped).Sort().ToHashSet();
		NList<byte> result = new(bytes2.Length);
		for (var i = 0; i < bytes2.Length; i += bwtBlockSize, Status[0]++)
		{
			if (bytes2.Length - i <= bwtBlockExtraSize)
				throw new DecoderFallbackException();
			var length = Min(bwtBlockSize, bytes2.Length - i - bwtBlockExtraSize);
			bytes2[i] &= (ValuesInByte >> 1) - 1;
			var firstPermutation = (int)bytes2.GetSlice(i, bwtBlockExtraSize).Progression(0L, (x, y) => unchecked((x << BitsPerByte) + y));
			i += bwtBlockExtraSize;
			result.AddRange(DecodeBWT2(bytes2.GetRange(i, length), hs, firstPermutation));
		}
		bytes2.Dispose();
		return result.ToNList(x => ByteIntervals[x]);
	}

	protected virtual NList<byte> DecodeBWT2(NList<byte> input, ListHashSet<byte> hs, int firstPermutation)
	{
		var mtfMemory = hs.ToNList();
		for (var i = 0; i < input.Length; i++)
		{
			var index = hs.IndexOf(input[i]);
			input[i] = mtfMemory[index];
			mtfMemory.SetRange(1, mtfMemory[..index]);
			mtfMemory[0] = input[i];
		}
		mtfMemory.Dispose();
		var sorted = input.ToNList((elem, index) => (elem, index)).Sort(x => x.elem);
		var convert = sorted.ToNList(x => x.index);
		sorted.Dispose();
		var result = RedStarLinq.NEmptyList<byte>(input.Length);
		var it = firstPermutation;
		for (var i = 0; i < input.Length; i++)
		{
			it = convert[it];
			result[i] = input[it];
		}
		convert.Dispose();
		return result;
	}

	public virtual NList<byte> DecodeZLE(Slice<short> byteList, ref int i, int bwtBlockSize)
	{
		if (i >= byteList.Length)
			throw new DecoderFallbackException();
		short zero = byteList[i++], zeroB = byteList[i++];
		NList<byte> result = new(bwtBlockSize);
		String zeroCode = ['1'];
		int length;
		for (; i < byteList.Length && result.Length < bwtBlockSize;)
		{
			while (i < byteList.Length && result.Length < bwtBlockSize && byteList[i] != zero && byteList[i] != zeroB)
				result.Add((byte)byteList[i++]);
			if (i >= byteList.Length || result.Length >= bwtBlockSize)
				break;
			zeroCode.Remove(1);
			length = 0;
			while (i < byteList.Length && result.Length + length < bwtBlockSize && (byteList[i] == zero || byteList[i] == zeroB))
			{
				zeroCode.Add(byteList[i++] == zero ? '0' : '1');
				length = (int)(new MpzT(zeroCode.ToString(), 2) - 1);
			}
			using var streamOfZeros = RedStarLinq.NFill((byte)zero, length);
			result.AddRange(streamOfZeros);
		}
		return result;
	}
}
