
namespace AresFLib;

public class AdaptiveHuffmanDecF
{
	private readonly DecodingF decoding = default!;
	protected ArithmeticDecoder ar = default!;
	protected List<ShortIntervalList> result = default!;
	protected NList<byte> skipped = default!;
	protected SumSet<uint> set = default!;
	protected List<Interval> uniqueList = default!;
	protected LZData lzData = default!;
	protected uint fileBase, nextWordLink;
	protected int lz, bwt, fullLength, counter;
	protected SumList lengthsSL, distsSL;
	protected int lzLength;
	protected uint firstIntervalDist;

	public AdaptiveHuffmanDecF(DecodingF decoding, ArithmeticDecoder ar, NList<byte> skipped, LZData lzData, int lz, int bwt, int counter)
	{
		this.decoding = decoding;
		this.ar = ar;
		this.skipped = skipped;
		this.lzData = lzData;
		this.lz = lz;
		this.bwt = bwt;
		this.counter = counter;
		Prerequisites();
		lengthsSL = lz != 0 ? new(RedStarLinq.Fill(1, (int)(lzData.Length.R == 0 ? lzData.Length.Max + 1 : lzData.Length.R == 1 ? lzData.Length.Threshold + 2 : lzData.Length.Max - lzData.Length.Threshold + 2))) : new();
		distsSL = lz != 0 ? new(RedStarLinq.Fill(1, (int)lzData.UseSpiralLengths + 1)) : new();
		firstIntervalDist = lz != 0 ? (lzData.Dist.R == 1 ? lzData.Dist.Threshold + 2 : lzData.Dist.Max + 1) + lzData.UseSpiralLengths : 0;
	}

	public virtual List<ShortIntervalList> Decode()
	{
		Prerequisites2();
		for (; counter > 0; counter--, Status[0]++)
			DecodeIteration();
		Current[0] += ProgressBarStep;
		return Postrequisites();
	}

	protected virtual void Prerequisites()
	{
		if (bwt != 0)
		{
			var skippedCount = (int)ar.ReadCount();
			var @base = skippedCount == 0 ? 1 : ar.ReadCount();
			if (skippedCount > @base || @base > decoding.GetFragmentLength())
				throw new DecoderFallbackException();
			for (var i = 0; i < skippedCount; i++)
				skipped.Add((byte)ar.ReadEqual(@base));
			counter -= skippedCount == 0 ? 1 : (skippedCount + 9) / 8;
		}
		fileBase = ar.ReadCount();
		if (counter < 0 || counter > decoding.GetFragmentLength() + (bwt == 0 ? 0 : decoding.GetFragmentLength() >> 8))
			throw new DecoderFallbackException();
		Status[0] = 0;
		StatusMaximum[0] = counter;
		set = [(uint.MaxValue, 1)];
	}

	protected virtual void Prerequisites2()
	{
		uniqueList = [];
		if (lz != 0)
		{
			set.Add((fileBase - 1, 1));
			uniqueList.Add(new(fileBase - 1, fileBase));
		}
		result = [];
	}

	protected virtual void DecodeIteration()
	{
		var readIndex = ReadFirst();
		if (!(lz != 0 && uniqueList[readIndex].Lower == fileBase - 1))
		{
			result.Add([uniqueList[readIndex]]);
			lzLength++;
			if (lz != 0 && distsSL.Length < firstIntervalDist)
				distsSL.Insert(distsSL.Length - ((int)lzData.UseSpiralLengths + 1), 1);
			return;
		}
		decoding.ProcessLZLength(lzData, lengthsSL, out readIndex, out var length);
		decoding.ProcessLZDist(lzData, distsSL, result.Length, out readIndex, out var dist, length, out var maxDist);
		if (decoding.ProcessLZSpiralLength(lzData, ref dist, out var spiralLength, maxDist))
			dist = 0;
		var start = (int)(result.Length - dist - length - 2);
		if (start < 0)
			throw new DecoderFallbackException();
		for (var k = (int)((length + 2) * (spiralLength + 1)); k > 0; k -= (int)length + 2)
			result.AddRange(result.GetSlice(start, (int)Min(length + 2, k)));
		lzLength++;
		if (lz != 0 && distsSL.Length < firstIntervalDist)
			new Chain((int)Min(firstIntervalDist - distsSL.Length, (length + 2) * (spiralLength + 1))).ForEach(x => distsSL.Insert(distsSL.Length - ((int)lzData.UseSpiralLengths + 1), 1));
	}

	protected virtual int ReadFirst()
	{
		var readIndex = ar.ReadPart(set);
		if (readIndex == set.Length - 1)
		{
			var actualIndex = ar.ReadEqual(fileBase);
			if (!set.TryAdd((actualIndex, 1), out readIndex))
				throw new DecoderFallbackException();
			uniqueList.Insert(readIndex, new Interval(actualIndex, fileBase));
		}
		else
			set.Increase(uniqueList[readIndex].Lower);
		FirstUpdateSet();
		return readIndex;
	}

	protected virtual void FirstUpdateSet() => set.Update(uint.MaxValue, Max(set.Length - 1, 1));

	protected virtual List<ShortIntervalList> Postrequisites() => result;
}
