
namespace AresFLib;

internal record class PPM(int TN) : IDisposable
{
	private ArithmeticEncoder ar = default!;
	private readonly List<NList<Interval>> result = [];
	private int doubleListsCompleted = 0;
	private readonly object lockObj = new();

	public void Dispose()
	{
		ar.Dispose();
		GC.SuppressFinalize(this);
	}

	public byte[] Encode(List<ShortIntervalList> input)
	{
		if (input.Length < 4)
			throw new EncoderFallbackException();
		ar = new();
		result.Replace(new List<NList<Interval>>(new NList<Interval>()));
		if (!new PPMInternal(input, result[0], 1, true, TN).Encode())
			throw new EncoderFallbackException();
		result[0].ForEach(x => ar.WritePart(x.Lower, x.Length, x.Base));
		ar.WriteEqual(1234567890, 4294967295);
		return ar;
	}

	public byte[] Encode(List<List<ShortIntervalList>> input, bool split = false)
	{
		if (!(input.Length >= 3 && input.GetSlice(..3).All(x => x.Length >= 4) || split))
			throw new EncoderFallbackException();
		var length = split ? input.Length : WordsListActualParts;
		Current[TN] = 0;
		CurrentMaximum[TN] = ProgressBarStep * (length - 1);
		ar = new();
		result.Replace(RedStarLinq.FillArray(length, _ => new NList<Interval>()));
		Parallel.For(0, length, i =>
		{
			if (!new PPMInternal(input[i], result[i], split ? 1 : i, i == WordsListActualParts - 1 || split, TN).Encode())
				throw new EncoderFallbackException();
			lock (lockObj)
			{
				doubleListsCompleted++;
				if (doubleListsCompleted != length)
					Current[TN] += ProgressBarStep;
			}
		});
		result.ForEach(l => l.ForEach(x => ar.WritePart(x.Lower, x.Length, x.Base)));
		input.GetSlice(length).ForEach(dl => dl.ForEach(l => l.ForEach(x => ar.WritePart(x.Lower, x.Length, x.Base))));
		ar.WriteEqual(1234567890, 4294967295);
		return ar;
	}
}

file record class PPMInternal(List<ShortIntervalList> Input, NList<Interval> Result, int N, bool LastDoubleList, int TN)
{
	private const int LZDictionarySize = 8388607;
	private int startPos = 1;
	private readonly SumSet<uint> globalSet = [], newItemsSet = [];
	private const int maxDepth = 12;
	private readonly LimitedQueue<NList<Interval>> buffer = new(maxDepth);
	private G.IEqualityComparer<NList<uint>> comparer = default!;
	private FastDelHashSet<NList<uint>> contextHS = default!;
	private HashList<int> lzhl = default!;
	private readonly List<SumSet<uint>> sumSets = [];
	private readonly SumSet<uint> lzPositions = [(uint.MaxValue, 100)];
	private readonly SumList lzLengthsSL = [];
	private uint lzCount, notLZCount, spaceCount, notSpaceCount;
	private readonly LimitedQueue<bool> spaceBuffer = new(maxDepth);
	private readonly LimitedQueue<uint> newItemsBuffer = new(maxDepth);
	private readonly NList<uint> context = new(maxDepth), context2 = new(maxDepth);
	private readonly SumSet<uint> set = [], excludingSet = [];
	private SumSet<uint> set2 = [];
	private readonly NList<Interval> intervalsForBuffer = [];
	private int nextTarget = 0;

	public bool Encode()
	{
		if (!(Input.Length >= 4 && Input[CreateVar(Input[0].Length >= 1 && Input[0][0] == LengthsApplied ? (int)Input[0][1].Base + 1 : 1, out startPos)].Length is 1 or 2 && Input[startPos][0].Length == 1 && CreateVar(Input[startPos][0].Base, out var inputBase) >= 2 && Input[startPos][^1].Length == 1 && Input.GetSlice(startPos + 1).All(x => x.Length == Input[startPos].Length && x[0].Length == 1 && x[0].Base == inputBase && (x.Length == 1 || x[1].Length == 1 && x[1].Base == Input[startPos][1].Base))))
			throw new EncoderFallbackException();
		if (LastDoubleList)
		{
			Status[TN] = 0;
			StatusMaximum[TN] = Input.Length - startPos;
		}
		for (var i = 0; i < Input[0].Length; i++)
			Result.Add(new(Input[0][i].Lower, Input[0][i].Length, Input[0][i].Base));
		if (N == 0)
		{
			Result.Add(new(Input[1][0].Lower, 1, 3));
			Result.WriteCount(inputBase);
			for (var i = 2; i < startPos; i++)
				for (var j = 0; j < Input[i].Length; j++)
					Result.Add(new(Input[i][j].Lower, Input[i][j].Length, Input[i][j].Base));
		}
		Result.WriteCount((uint)(Input.Length - startPos));
		Result.WriteCount((uint)Min(LZDictionarySize, FragmentLength));
		PrepareFields(inputBase);
		for (var i = startPos; i < Input.Length; i++, _ = LastDoubleList ? Status[TN] = i : 0)
		{
			var item = Input[i][0].Lower;
			Input.GetSlice(Max(startPos, i - maxDepth)..i).ForEach((x, index) => context.SetOrAdd(index, x[0].Lower));
			context.Reverse();
			context2.Replace(context);
			if (i < nextTarget)
				goto l1;
			intervalsForBuffer.Clear();
			if (context.Length == maxDepth && i >= (maxDepth << 1) + startPos && ProcessLZ(context, i) && i < nextTarget)
				goto l1;
			set.Clear();
			excludingSet.Clear();
			Escape(item, out var sum, out var frequency);
			ProcessFrequency(item, ref sum, ref frequency);
			ProcessBuffers(i);
		l1:
			var contextLength = context2.Length;
			Increase(context2, context, item, out var hlIndex);
			if (contextLength == maxDepth)
				lzhl.SetOrAdd((i - startPos - maxDepth) % LZDictionarySize, hlIndex);
		}
		while (buffer.Length != 0)
			buffer.Dequeue().ForEach(x => Result.Add(new(x.Lower, x.Length, x.Base)));
		return true;
	}

	private void PrepareFields(uint inputBase)
	{
		globalSet.Clear();
		if (N == 2)
			newItemsSet.Clear();
		else
			newItemsSet.Replace(new Chain((int)inputBase).Convert(x => ((uint)x, 1)));
		buffer.Clear();
		comparer = N == 2 ? new NListEComparer<uint>() : new EComparer<NList<uint>>((x, y) => x.Equals(y), x => unchecked(x.Progression(17 * 23 + x.Length, (x, y) => x * 23 + y.GetHashCode())));
		contextHS = new(comparer);
		lzhl = [];
		sumSets.Clear();
		lzLengthsSL.Replace([1]);
		lzCount = notLZCount = spaceCount = notSpaceCount = 1;
		spaceBuffer.Clear();
		newItemsBuffer.Clear();
		context.Clear();
		context2.Clear();
		set.Clear();
		excludingSet.Clear();
		intervalsForBuffer.Clear();
		nextTarget = 0;
	}

	private void Escape(uint item, out long sum, out int frequency)
	{
		for (; context.Length > 0 && !contextHS.TryGetIndexOf(context, out _); context.RemoveAt(^1)) ;
		sum = 0;
		frequency = 0;
		for (; context.Length > 0 && contextHS.TryGetIndexOf(context, out var index) && (sum = set.Replace(sumSets[index]).ExceptWith(excludingSet).GetLeftValuesSum(item, out frequency)) >= 0 && frequency == 0; context.RemoveAt(^1), excludingSet.UnionWith(set))
			if (set.Length != 0)
				intervalsForBuffer.Add(new((uint)set.ValuesSum, (uint)set.Length * 100, (uint)(set.ValuesSum + set.Length * 100)));
		if (set.Length == 0 || context.Length == 0)
		{
			excludingSet.ForEach(x => excludingSet.Update(x.Key, globalSet.TryGetValue(x.Key, out var newValue) ? newValue : throw new EncoderFallbackException()));
			set2 = globalSet.ExceptWith(excludingSet);
		}
		else
			set2 = set;
	}

	private void ProcessFrequency(uint item, ref long sum, ref int frequency)
	{
		if (frequency == 0)
			sum = set2.GetLeftValuesSum(item, out frequency);
		if (frequency == 0)
		{
			if (set2.Length != 0)
				intervalsForBuffer.Add(new((uint)set2.ValuesSum, (uint)set2.Length * 100, (uint)(set2.ValuesSum + set2.Length * 100)));
			if (N != 2)
			{
				intervalsForBuffer.Add(new((uint)newItemsSet.IndexOf(item), (uint)newItemsSet.Length));
				newItemsSet.RemoveValue(item);
				newItemsBuffer.Enqueue(item);
			}
		}
		else
		{
			intervalsForBuffer.Add(new(0, (uint)set2.ValuesSum, (uint)(set2.ValuesSum + set2.Length * 100)));
			intervalsForBuffer.Add(new((uint)sum, (uint)frequency, (uint)set2.ValuesSum));
			newItemsBuffer.Enqueue(uint.MaxValue);
		}
		if (set.Length == 0 || context.Length == 0)
			globalSet.UnionWith(excludingSet);
	}

	private void ProcessBuffers(int i)
	{
		var isSpace = false;
		if (N == 2)
		{
			isSpace = Input[i][1].Lower != 0;
			uint bufferSpaces = (uint)spaceBuffer.Count(true), bufferNotSpaces = (uint)spaceBuffer.Count(false);
			intervalsForBuffer.Add(new(isSpace ? notSpaceCount + bufferNotSpaces : 0, isSpace ? spaceCount + bufferSpaces : notSpaceCount + bufferNotSpaces, notSpaceCount + spaceCount + (uint)spaceBuffer.Length));
		}
		else
			for (var j = 1; j < Input[i].Length; j++)
				intervalsForBuffer.Add(new(Input[i][j].Lower, Input[i][j].Length, Input[i][j].Base));
		if (buffer.IsFull)
			buffer.Dequeue().ForEach(x => Result.Add(new(x.Lower, x.Length, x.Base)));
		buffer.Enqueue(intervalsForBuffer.Copy());
		if (N == 2 && spaceBuffer.IsFull)
		{
			var space2 = spaceBuffer.Dequeue();
			if (space2)
				spaceCount++;
			else
				notSpaceCount++;
		}
		spaceBuffer.Enqueue(isSpace);
	}

	bool ProcessLZ(NList<uint> context, int curPos)
	{
		if (!buffer.IsFull)
			return false;
		var bestPos = -1;
		var bestLength = -1;
		var contextIndex = contextHS.IndexOf(context);
		var indexes = lzhl.IndexesOf(contextIndex).Sort();
		for (var i = 0; i < indexes.Length; i++)
		{
			var pos = indexes[i];
			var dist = (pos - (curPos - startPos - maxDepth)) % LZDictionarySize + curPos - startPos - maxDepth;
			int length;
			for (length = -maxDepth; length < Input.Length - startPos - curPos && RedStarLinq.Equals(Input[curPos + length], Input[dist + maxDepth + startPos + length], (x, y) => x.Lower == y.Lower); length++) ;
			if (curPos - (dist + maxDepth + startPos) >= 2 && length > bestLength)
			{
				bestPos = pos;
				bestLength = length;
			}
		}
		if (bestPos == -1)
		{
			if (buffer.IsFull)
			{
				Result.Add(new(0, notLZCount, lzCount + notLZCount));
				notLZCount++;
			}
			return false;
		}
		Result.Add(new(notLZCount, lzCount, lzCount + notLZCount));
		lzCount++;
		if (CreateVar(lzPositions.GetLeftValuesSum((uint)bestPos, out var posFrequency), out var sum) >= 0 && posFrequency != 0)
		{
			Result.Add(new((uint)sum, (uint)posFrequency, (uint)lzPositions.ValuesSum));
			lzPositions.Update((uint)bestPos, posFrequency + 100);
		}
		else
		{
			Result.Add(new((uint)lzPositions.GetLeftValuesSum(uint.MaxValue, out var escapeFrequency), (uint)escapeFrequency, (uint)lzPositions.ValuesSum));
			lzPositions.Update(uint.MaxValue, escapeFrequency + 100);
			Result.Add(new((uint)bestPos, (uint)Min(curPos - startPos - maxDepth, LZDictionarySize - 1)));
			lzPositions.Add((uint)bestPos, 100);
		}
		if (bestLength < lzLengthsSL.Length - 1)
		{
			Result.Add(new((uint)lzLengthsSL.GetLeftValuesSum(bestLength, out var frequency), (uint)frequency, (uint)lzLengthsSL.ValuesSum));
			lzLengthsSL.Increase(bestLength);
		}
		else
		{
			Result.Add(new((uint)(lzLengthsSL.ValuesSum - lzLengthsSL[^1]), (uint)lzLengthsSL[^1], (uint)lzLengthsSL.ValuesSum));
			lzLengthsSL.Increase(lzLengthsSL.Length - 1);
			foreach (var bit in EncodeFibonacci((uint)(bestLength - lzLengthsSL.Length + 2)))
				Result.Add(new(bit ? 1u : 0, 2));
			new Chain(bestLength - lzLengthsSL.Length + 1).ForEach(x => lzLengthsSL.Insert(lzLengthsSL.Length - 1, 1));
		}
		buffer.Clear();
		spaceBuffer.Clear();
		if (N != 2)
			newItemsBuffer.Filter(x => x != uint.MaxValue).ForEach(x => newItemsSet.Add((x, 1)));
		newItemsBuffer.Clear();
		nextTarget = curPos + bestLength;
		return true;
	}

	void Increase(NList<uint> context, NList<uint> successContext, uint item, out int outIndex)
	{
		outIndex = -1;
		for (; context.Length > 0 && contextHS.TryAdd(context.Copy(), out var index); context.RemoveAt(^1))
		{
			if (outIndex == -1)
				outIndex = index;
			sumSets.SetOrAdd(index, [(item, 100)]);
		}
		var successLength = context.Length;
		_ = context.Length == 0 ? null : successContext.Replace(context).RemoveAt(^1);
		for (; context.Length > 0 && contextHS.TryGetIndexOf(context, out var index); context.RemoveAt(^1), _ = context.Length == 0 ? null : successContext.RemoveAt(^1))
		{
			if (outIndex == -1)
				outIndex = index;
			if (!sumSets[index].TryGetValue(item, out var itemValue))
			{
				sumSets[index].Add(item, 100);
				continue;
			}
			else if (context.Length == 1 || itemValue > 100)
			{
				sumSets[index].Update(item, itemValue + (int)Max(Round((double)100 / (successLength - context.Length + 1)), 1));
				continue;
			}
			var successIndex = contextHS.IndexOf(successContext);
			if (!sumSets[successIndex].TryGetValue(item, out var successValue))
				successValue = 100;
			var step = (double)(sumSets[index].ValuesSum + sumSets[index].Length * 100) * successValue / (sumSets[index].ValuesSum + sumSets[successIndex].ValuesSum + sumSets[successIndex].Length * 100 - successValue);
			sumSets[index].Update(item, (int)(Max(Round(step), 1) + itemValue));
		}
		if (globalSet.TryGetValue(item, out var globalValue))
			globalSet.Update(item, globalValue + (int)Max(Round((double)100 / (successLength + 1)), 1));
		else
			globalSet.Add(item, 100);
	}
}

internal record class PPMBits(int TN)
{
	private const int LZDictionarySize = 8388607;

	public byte[] Encode(BitList input)
	{
		Status[TN] = 0;
		StatusMaximum[TN] = input.Length;
		using ArithmeticEncoder ar = new();
		ar.WriteCount((uint)input.Length);
		var dicsize = LZDictionarySize << 3;
		ar.WriteCount((uint)dicsize);
		(uint Zeros, uint Units) globalSet = (1, 1);
		var maxDepth = 96;
		LimitedQueue<NList<Interval>> buffer = new(maxDepth);
		var comparer = new EComparer<BitList>((x, y) => x.Equals(y), x => unchecked(x.Progression(17 * 23 + x.Length, (x, y) => x * 23 + y.GetHashCode())));
		FastDelHashSet<BitList> contextHS = new(comparer);
		HashList<BitList> lzhl = new(comparer);
		List<(uint Zeros, uint Units)> sumSets = [];
		uint lzCount = 1, notLZCount = 1;
		var nextTarget = 0;
		for (var i = 0; i < input.Length; i++, Status[TN]++)
		{
			var item = input[i];
			var context = input.GetRange(Max(0, i - maxDepth)..i).Reverse();
			var context2 = context.Copy();
			if (i < nextTarget)
				goto l1;
			var index = -1;
			NList<Interval> intervals = [];
			if (context.Length == maxDepth && i >= maxDepth << 1 && ProcessLZ(context, item, i) && i < nextTarget)
				goto l1;
			for (; context.Length > 0 && !contextHS.TryGetIndexOf(context, out index); context.RemoveAt(^1)) ;
			if (context.Length == 0)
				intervals.Add(new(item ? globalSet.Zeros : 0, item ? globalSet.Units : globalSet.Zeros, globalSet.Zeros + globalSet.Units));
			else
				intervals.Add(new(item ? sumSets[index].Zeros : 0, item ? sumSets[index].Units : sumSets[index].Zeros, sumSets[index].Zeros + sumSets[index].Units));
			if (buffer.IsFull)
				buffer.Dequeue().ForEach(x => ar.WritePart(x.Lower, x.Length, x.Base));
			buffer.Enqueue(intervals);
		l1:
			if (context2.Length == maxDepth)
				lzhl.SetOrAdd((i - maxDepth) % dicsize, context2.Copy());
			Increase(context2, item);
			context.Dispose();
			context2.Dispose();
		}
		while (buffer.Length != 0)
			buffer.Dequeue().ForEach(x => ar.WritePart(x.Lower, x.Length, x.Base));
		bool ProcessLZ(BitList context, bool item, int i)
		{
			if (!buffer.IsFull)
				return false;
			var bestDist = -1;
			var bestLength = -1;
			foreach (var pos in lzhl.IndexesOf(context))
			{
				var dist = (pos - (i - maxDepth)) % dicsize + i - maxDepth;
				int length;
				for (length = -maxDepth; length < input.Length - i && input[i + length] == input[dist + maxDepth + length]; length++) ;
				if (i - (dist + maxDepth) >= 2 && length > bestLength)
				{
					bestDist = dist;
					bestLength = length;
				}
			}
			if (bestDist == -1)
			{
				if (buffer.IsFull)
				{
					ar.WritePart(0, notLZCount, lzCount + notLZCount);
					notLZCount++;
				}
				return false;
			}
			ar.WritePart(notLZCount, lzCount, lzCount + notLZCount);
			lzCount++;
			ar.WriteEqual((uint)(i - (bestDist + maxDepth) - 2), (uint)Min(i - maxDepth, dicsize - 1));
			ar.WriteFibonacci((uint)bestLength + 1);
			buffer.Clear();
			nextTarget = i + bestLength;
			return true;
		}
		void Increase(BitList context, bool item)
		{
			for (; context.Length > 0 && !contextHS.TryGetIndexOf(context, out var index); context.RemoveAt(^1))
			{
				contextHS.TryAdd(context.Copy(), out index);
				sumSets.SetOrAdd(index, item ? (1, (uint)(GetArrayLength(context.Length, 2) + 1)) : ((uint)(GetArrayLength(context.Length, 2) + 1), 1));
			}
			for (; context.Length > 0 && contextHS.TryGetIndexOf(context, out var index); sumSets[index] = (Clamp(sumSets[index].Zeros + (item ? 0xffffffff : 1), 1, 40), Clamp(sumSets[index].Units + (item ? 1 : 0xffffffff), 1, 40)), context.RemoveAt(^1)) ;
			globalSet = (globalSet.Zeros + (item ? 0u : 1), globalSet.Units + (item ? 1u : 0));
		}
		return ar;
	}
}
