﻿
namespace AresFLib;

public class Decoding2F
{
	protected DecodingF decoding = default!;
	protected ArithmeticDecoder ar = default!;
	protected int maxFrequency, frequencyCount, hf, bwt, lz, counter;
	protected uint lzRDist, lzMaxDist, lzThresholdDist, lzRLength, lzMaxLength, lzThresholdLength, lzUseSpiralLengths, lzRSpiralLength, lzMaxSpiralLength, lzThresholdSpiralLength;
	protected MethodDataUnit lzDist = new(), lzLength = new(), lzSpiralLength = new();
	protected LZData lzData = default!;
	protected NList<uint> arithmeticMap = default!;
	protected NList<Interval> uniqueList = default!;
	protected NList<byte> skipped = default!;

	public Decoding2F(DecodingF decoding, ArithmeticDecoder ar, int hf, int bwt, int lz)
	{
		this.decoding = decoding;
		this.ar = ar;
		this.hf = hf;
		this.bwt = bwt;
		this.lz = lz;
		counter = (int)ar.ReadCount() - 1;
		lzDist = new();
		lzLength = new();
		lzSpiralLength = new();
		maxFrequency = 0;
		frequencyCount = 0;
		arithmeticMap = [];
		uniqueList = [];
		skipped = [];
	}

	public virtual List<ShortIntervalList> Decode()
	{
		if (lz != 0)
		{
			var counter2 = 7;
			lzRDist = ar.ReadEqual(3);
			lzMaxDist = ar.ReadCount();
			if (lzRDist != 0)
			{
				lzThresholdDist = ar.ReadEqual(lzMaxDist + 1);
				counter2++;
			}
			lzDist = new(lzRDist, lzMaxDist, lzThresholdDist);
			lzRLength = ar.ReadEqual(3);
			lzMaxLength = ar.ReadCount(16);
			if (lzRLength != 0)
			{
				lzThresholdLength = ar.ReadEqual(lzMaxLength + 1);
				counter2++;
			}
			lzLength = new(lzRLength, lzMaxLength, lzThresholdLength);
			if (lzMaxDist == 0 && lzMaxLength == 0 && ar.ReadEqual(2) == 0)
			{
				lz = 0;
				goto l0;
			}
			lzUseSpiralLengths = ar.ReadEqual(2);
			if (lzUseSpiralLengths == 1)
			{
				lzRSpiralLength = ar.ReadEqual(3);
				lzMaxSpiralLength = ar.ReadCount(16);
				counter2 += 3;
				if (lzRSpiralLength != 0)
				{
					lzThresholdSpiralLength = ar.ReadEqual(lzMaxSpiralLength + 1);
					counter2++;
				}
				lzSpiralLength = new(lzRSpiralLength, lzMaxSpiralLength, lzThresholdSpiralLength);
			}
		l0:
			counter -= GetArrayLength(counter2, 8);
		}
		lzData = new(lzDist, lzLength, lzUseSpiralLengths, lzSpiralLength);
		return ProcessHuffman();
	}

	protected virtual List<ShortIntervalList> ProcessHuffman()
	{
		List<ShortIntervalList> compressedList;
		if (hf is 2 or 3 or 4)
		{
			compressedList = DecodeAdaptive();
			goto l1;
		}
		if (hf == 5)
		{
			var counter2 = 4;
			maxFrequency = (int)ar.ReadCount() + 1;
			frequencyCount = (int)ar.ReadCount() + 1;
			if (maxFrequency > GetFragmentLength() || frequencyCount > GetFragmentLength())
				throw new DecoderFallbackException();
			Status[0] = 0;
			StatusMaximum[0] = frequencyCount;
			var @base = (uint)ValuesInByte;
			if (maxFrequency > frequencyCount * 2 || frequencyCount <= ValuesInByte)
			{
				arithmeticMap.Add((uint)maxFrequency);
				var prev = (uint)maxFrequency;
				for (var i = 0; i < frequencyCount; i++, Status[0]++)
				{
					counter2++;
					uniqueList.Add(new(ar.ReadEqual(@base), @base));
					if (i == 0) continue;
					prev = ar.ReadEqual(prev) + 1;
					counter2++;
					arithmeticMap.Add(arithmeticMap[^1] + prev);
				}
			}
			else
				for (var i = 0; i < frequencyCount; i++, Status[0]++)
				{
					uniqueList.Add(new((uint)i, (uint)frequencyCount));
					counter2++;
					arithmeticMap.Add((arithmeticMap.Length == 0 ? 0 : arithmeticMap[^1]) + ar.ReadEqual((uint)maxFrequency) + 1);
				}
			if (lz != 0)
				arithmeticMap.Add(GetHuffmanBase(arithmeticMap[^1]));
			counter -= GetArrayLength(counter2, 8);
			if (bwt != 0)
			{
				var skippedCount = (int)ar.ReadCount();
				for (var i = 0; i < skippedCount; i++)
					skipped.Add((byte)ar.ReadEqual(@base));
				counter -= (skippedCount + 9) / 8;
			}
		}
		else
		{
			uniqueList.AddRange(RedStarLinq.Fill(ValuesInByte, index => new Interval((uint)index, ValuesInByte)));
			arithmeticMap.AddRange(RedStarLinq.Fill(ValuesInByte, index => (uint)(index + 1)));
			if (lz != 0)
				arithmeticMap.Add(GetHuffmanBase(ValuesInByte));
		}
		if (counter is < 0 || counter > GetFragmentLength() + GetFragmentLength() / 1000)
			throw new DecoderFallbackException();
		HuffmanData huffmanData = new(maxFrequency, frequencyCount, arithmeticMap, uniqueList);
		Current[0] += ProgressBarStep;
		compressedList = decoding.ReadCompressedList(huffmanData, bwt, lzData, lz, counter);
	l1:
		if (bwt != 0)
		{
			Current[0] += ProgressBarStep;
			compressedList = decoding.DecodeBWT(compressedList, skipped);
		}
		return compressedList;
	}

	protected virtual List<ShortIntervalList> DecodeAdaptive() => new AdaptiveHuffmanDecGlobal(decoding.CreateGlobalDecoding(), ar, skipped, lzData, lz, bwt, counter).Decode();

	protected virtual uint GetHuffmanBase(uint oldBase) => GetBaseWithBuffer(oldBase, false);
}
