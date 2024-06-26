
namespace AresFLib;
internal record class LZMA(int TN)
{
	private readonly int threadsCount = Environment.ProcessorCount;

	public byte[] Encode(List<ShortIntervalList> input)
	{
		if (!(input.Length >= 3 && CreateVar(input[1][0].Base, out var originalBase) <= ValuesInByte && input.GetSlice(2).All(x => x[0].Base == originalBase)))
			throw new EncoderFallbackException();
		using BitList bitList = new(input.GetSlice(1).ToNList(x => (byte)x[0].Lower));
		return Encode(bitList);
	}

	public byte[] Encode(BitList bitList)
	{
		Current[TN] = 0;
		CurrentMaximum[TN] = ProgressBarStep * 2;
		var indexCodes = RedStarLinq.PNFill(bitList.Length - 23, index => bitList.GetSmallRange(index, 24)).PGroup(TN).Filter(X => X.Group.Length >= 2).NSort(x => x.Key).PToArray(col => col.Group.NSort());
		var startKGlobal = 24;
		var repeatsInfo = RedStarLinq.FillArray(threadsCount, _ => new Dictionary<uint, (uint dist, uint length, uint spiralLength)>());
		Status[TN] = 0;
		StatusMaximum[TN] = indexCodes.Sum(x => x.Length);
		Current[TN] += ProgressBarStep;
		TreeSet<int> maxReached = [];
		Dictionary<int, int> maxReachedLengths = [];
		uint useSpiralLengths = 0;
		var maxLevel = Max(BitsCount(LZDictionarySize * BitsPerByte) / 2 - 5, 0);
		var lockObj = RedStarLinq.FillArray(threadsCount, _ => new object());
		Parallel.ForEach(indexCodes, x => FindMatchesRecursive(x, 0));
		var (blockStartValue, blockEscapeValue, bitsCount, blockStartIndexes) = LZMABlockStart(bitList);
		var blockStart = new BitList(bitsCount, blockStartValue).ToList(x => new ShortIntervalList() { new(x ? 1u : 0, 2) });
		var repeatsInfoSum = repeatsInfo.JoinIntoSingle().ToNList();
		LZRequisites(bitList.Length, BitsPerByte, repeatsInfoSum, out var boundIndex, out var repeatsInfoList2, out var starts, out var dists, out var lengths, out var spiralLengths, out var maxDist, out var maxLength, out var maxSpiralLength, out var rDist, out var thresholdDist, out var rLength, out var thresholdLength, out var rSpiralLength, out var thresholdSpiralLength, PrimitiveType.UIntType);
		var result = bitList.ToList(x => new ShortIntervalList() { new(x ? 1u : 0, 2) });
		Status[TN] = 0;
		StatusMaximum[TN] = starts.Length;
		Current[TN] += ProgressBarStep;
		BitList elementsReplaced = new(result.Length, false);
		WriteLZMatches(result, blockStart, useSpiralLengths, starts[..boundIndex], dists, lengths, spiralLengths, maxDist, maxLength, maxSpiralLength, rDist, thresholdDist, rLength, thresholdLength, rSpiralLength, thresholdSpiralLength, elementsReplaced);
		var sortedRepeatsInfo2 = repeatsInfoList2.PConvert(l => l.PNBreak(x => x.Key, x => (x.Value.dist, x.Value.length, x.Value.spiralLength)));
		repeatsInfoList2.ForEach(x => x.Dispose());
		repeatsInfoList2.Dispose();
		var brokenRepeatsInfo = sortedRepeatsInfo2.PConvert(l => (l.Item1, l.Item2.PNBreak()));
		sortedRepeatsInfo2.ForEach(x => x.Item2.Dispose());
		sortedRepeatsInfo2.Dispose();
		Parallel.ForEach(brokenRepeatsInfo, x => WriteLZMatches(result, blockStart, useSpiralLengths, x.Item1, x.Item2.Item1, x.Item2.Item2, x.Item2.Item3, maxDist, maxLength, maxSpiralLength, rDist, thresholdDist, rLength, thresholdLength, rSpiralLength, thresholdSpiralLength, elementsReplaced, false));
		if (blockStartIndexes.Length != 0)
		{
			var temp = result.GetRange(0, blockStartIndexes[0]);
			var tempReplaced = elementsReplaced.GetRange(0, blockStartIndexes[0]);
			var blockEscape = new BitList(bitsCount, blockEscapeValue).Convert(x => new ShortIntervalList() { new(x ? 1u : 0, 2) });
			BitList blockReplaced = new(bitsCount, 0);
			for (var i = 1; i < blockStartIndexes.Length; i++)
			{
				temp.AddRange(blockEscape);
				temp.AddRange(result.GetRange(blockStartIndexes[i - 1]..blockStartIndexes[i]));
				tempReplaced.AddRange(blockReplaced);
				tempReplaced.AddRange(elementsReplaced.GetRange(blockStartIndexes[i - 1]..blockStartIndexes[i]));
			}
			temp.AddRange(blockEscape);
			temp.AddRange(result.GetRange(blockStartIndexes[^1]..));
			tempReplaced.AddRange(blockReplaced);
			tempReplaced.AddRange(elementsReplaced.GetRange(blockStartIndexes[^1]..));
			blockEscape.Dispose();
			blockReplaced.Dispose();
			result.Dispose();
			elementsReplaced.Dispose();
			result = temp;
			elementsReplaced = tempReplaced;
		}
		result.FilterInPlace((x, index) => index == 0 || !elementsReplaced[index]);
		elementsReplaced.Dispose();
		List<Interval> c = [new Interval((uint)rDist, 3)];
		c.WriteCount(maxDist, 24);
		if (rDist != 0)
			c.Add(new(thresholdDist, maxDist + 1));
		c.Add(new Interval((uint)rLength, 3));
		c.WriteCount(maxLength, 24);
		if (rLength != 0)
			c.Add(new(thresholdLength, maxLength + 1));
		if (maxDist == 0 && maxLength == 0)
			c.Add(new(1, 2));
		c.Add(new Interval(useSpiralLengths, 2));
		if (useSpiralLengths == 1)
		{
			c.Add(new Interval((uint)rSpiralLength, 3));
			c.WriteCount(maxSpiralLength, 16);
			if (rSpiralLength != 0)
				c.Add(new(thresholdSpiralLength, maxSpiralLength + 1));
		}
		var cSplit = c.SplitIntoEqual(8).Convert(x => new ShortIntervalList(x));
		c.Dispose();
		result.Insert(0, cSplit);
		using ArithmeticEncoder ar = new();
		if (!new AdaptiveHuffmanBits(TN).Encode(ar, result, cSplit.Length))
			throw new EncoderFallbackException();
		cSplit.Dispose();
		return ar;
		void FindMatchesRecursive(uint[] ic, int level)
		{
			if (level < maxLevel)
			{
				var nextCodes = ic.GetSlice(..Max(ic.FindLastIndex(x => x <= bitList.Length - level * BitsPerByte - startKGlobal), 0)).GroupIndexes(iIC => bitList.GetSmallRange((int)iIC + startKGlobal, BitsPerByte)).FilterInPlace(x => x.Length >= 2).Convert(x => x.ToArray(index => ic[index]).NSort());
				var nextCodes2 = nextCodes.JoinIntoSingle().ToHashSet();
				ic = ic.Filter(x => !nextCodes2.Contains(x)).ToArray();
				nextCodes.ForEach(x => FindMatchesRecursive(x, level + 1));
			}
			if (ic.Length > 1)
				FindMatches(ic, level * BitsPerByte + startKGlobal);
		}
		void FindMatches(uint[] ic, int startK)
		{
			for (var i = 1; i < ic.Length; i++, Status[TN]++)
			{
				var iIC = (int)ic[i];
				if (maxReached.Length != 0 && Lock(lockObj, () => CreateVar(maxReached.IndexOfNotLess(iIC), out var mr) >= 1 && maxReachedLengths[maxReached[mr - 1]] > iIC - maxReached[mr - 1]))
					continue;
				var matches = ic.GetSlice((Array.FindLastIndex(ic, i - 1, x => iIC - x >= LZDictionarySize * BitsPerByte) + 1)..i).Filter(jIC => iIC - jIC >= 2 && RedStarLinq.Equals(bitList.GetRange(iIC, startK), bitList.GetRange((int)jIC, startK))).ToNList();
				var ub = bitList.Length - iIC - 1;
				if (matches.Length == 0 || ub < startK)
					continue;
				var lastMatch = (int)matches[^1];
				var k = startK;
				for (; k <= ub && matches.Length > 1; k++)
				{
					lastMatch = (int)matches[^1];
					matches.FilterInPlace(jIC => bitList[iIC + k] == bitList[(int)(jIC + k)]);
					if (k == ub || matches.Length == 0 || iIC - matches[^1] >= (iIC - lastMatch) << 3 && lastMatch - matches[^1] >= 64) break;
				}
				if (matches.Length == 1)
				{
					lastMatch = (int)matches[^1];
					var ub2 = Min(ub, (int)Min((long)(iIC - lastMatch) * (ushort.MaxValue + 1) - 1, int.MaxValue));
					for (; k <= ub2 && bitList[iIC + k] == bitList[lastMatch + k]; k++) ;
				}
				if (k * Log(2) < Log(21) + Log(LZDictionarySize * BitsPerByte) + Log(k))
					continue;
				var sl = (ushort)Clamp(k / (iIC - lastMatch) - 1, 0, ushort.MaxValue);
				UpdateRepeatsInfo(repeatsInfo, lockObj, threadsCount, iIC, (uint)Max(iIC - lastMatch - k, 0), (uint)Min(k - 2, iIC - lastMatch - 2), sl, PrimitiveType.UIntType);
				if (sl > 0)
					useSpiralLengths = 1;
				if (k > ub || sl == ushort.MaxValue)
					Lock(lockObj, () => (maxReached.Add(iIC), maxReachedLengths.TryAdd(iIC, k)));
			}
		}
	}

	private static (uint Value, uint Escape, int bitsCount, NList<int> ValueIndexes) LZMABlockStart(BitList bitList)
	{
		using NList<int> ranges = [];
		for (var i = 2; i <= BitsPerByte; i++)
		{
			ranges.Replace(RedStarLinq.PNFill(bitList.Length - (i - 1), index => (int)bitList.GetSmallRange(index, i)));
			using var hs = new Chain(i).ToHashSet();
			hs.ExceptWith(ranges);
			if (hs.Length != 0)
				return ((uint)hs[0], uint.MaxValue, i, []);
		}
		using var groups = ranges.Group();
		return ((uint)CreateVar(groups.FindMin(x => x.Length)!.Key, out var value), (uint)groups.FilterInPlace(x => x.Key != value).FindMin(x => x.Length)!.Key, BitsPerByte, ranges.IndexesOf(value));
	}

	private void WriteLZMatches(List<ShortIntervalList> result, List<ShortIntervalList> blockStart, uint useSpiralLengths, NList<uint> starts, NList<uint> dists, NList<uint> lengths, NList<uint> spiralLengths, uint maxDist, uint maxLength, uint maxSpiralLength, int rDist, uint thresholdDist, int rLength, uint thresholdLength, int rSpiralLength, uint thresholdSpiralLength, BitList elementsReplaced, bool changeBase = true)
	{
		double statesNumLog1, statesNumLog2;
		for (var i = 0; i < starts.Length; i++, Status[TN]++)
		{
			uint iDist = dists[i], iLength = lengths[i], iSpiralLength = spiralLengths[i];
			var localMaxLength = (iLength + 2) * (iSpiralLength + 1) - 2;
			var iStart = (int)starts[i];
			if (elementsReplaced.IndexOf(true, iStart, Min((int)localMaxLength + 3, elementsReplaced.Length - iStart)) != -1)
				continue;
			statesNumLog1 = Log(2) * (localMaxLength + 2);
			statesNumLog2 = Log(2) * blockStart.Length;
			statesNumLog2 += StatesNumLogSum(iDist, rDist, maxDist, thresholdDist, useSpiralLengths);
			statesNumLog2 += StatesNumLogSum(iLength, rLength, maxLength, thresholdLength);
			if (useSpiralLengths == 1 && iLength < localMaxLength)
				statesNumLog2 += StatesNumLogSum(iSpiralLength, rSpiralLength, maxSpiralLength, thresholdSpiralLength);
			if (statesNumLog1 <= statesNumLog2)
				continue;
			var b = blockStart.Copy();
			b[^1] = new(b[^1]);
			WriteLZValue(b[^1], iLength, rLength, maxLength, thresholdLength);
			var maxDist2 = Min(maxDist, (uint)(iStart - iLength - 2));
			if (useSpiralLengths == 0)
				WriteLZValue(b[^1], iDist, rDist, maxDist2, thresholdDist);
			else if (iLength >= localMaxLength)
				WriteLZValue(b[^1], iDist, rDist, maxDist2, thresholdDist, 1);
			else
			{
				if (rDist == 0 || maxDist2 < thresholdDist)
					b[^1].Add(new(maxDist2 + 1, maxDist2 + 2));
				else if (rDist == 1)
				{
					b[^1].Add(new(thresholdDist + 1, thresholdDist + 2));
					b[^1].Add(new(maxDist2 - thresholdDist, maxDist2 - thresholdDist + 1));
				}
				else
				{
					b[^1].Add(new(maxDist2 - thresholdDist + 1, maxDist2 - thresholdDist + 2));
					b[^1].Add(new(thresholdDist, thresholdDist + 1));
				}
			}
			if (useSpiralLengths == 1 && iLength < localMaxLength)
				WriteLZValue(b[^1], iSpiralLength, rSpiralLength, maxSpiralLength, thresholdSpiralLength);
			result.SetRange(iStart, b);
			elementsReplaced.SetAll(true, iStart + b.Length, (int)localMaxLength + 2 - b.Length);
		}
		starts.Dispose();
		dists.Dispose();
		lengths.Dispose();
		spiralLengths.Dispose();
		if (!changeBase)
			return;
		Parallel.For(2, result.Length, i =>
		{
			if (!elementsReplaced[i] && (i == result.Length - 1 || !elementsReplaced[i + 1]))
			{
				var first = result[i][0];
				var newBase = GetBaseWithBuffer(first.Base);
				result[i] = newBase == 269 ? ByteIntervals2[(int)first.Lower] : new(result[i]) { [0] = new(first.Lower, first.Length, newBase) };
			}
		});
	}
}
