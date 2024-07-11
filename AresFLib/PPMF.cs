
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

file record class PPMInternal(List<ShortIntervalList> Input, NList<Interval> Result, int BlockIndex, bool LastBlock, int TN)
{
	private const int LZDictionarySize = 8388607;
	private int startPos = 1;
	private uint inputBase;
	private ShortIntervalList appliedMethods = default!;
	private readonly SumSet<uint> globalFreqTable = [], newItemsFreqTable = [];
	private const int maxContextDepth = 12;
	private readonly LimitedQueue<NList<Interval>> buffer = new(maxContextDepth);
	private G.IEqualityComparer<NList<uint>> comparer = default!;
	private FastDelHashSet<NList<uint>> contextSet = default!;
	private HashList<int> lzBuffer = default!;
	private readonly List<SumSet<uint>> contextFreqTableByLevel = [];
	private readonly SumSet<uint> lzPositions = [(uint.MaxValue, 100)];
	private readonly SumList lzLengths = [];
	private uint lzCount, notLZCount, spaceCount, notSpaceCount;
	private readonly LimitedQueue<bool> spaceBuffer = new(maxContextDepth);
	private readonly LimitedQueue<uint> newItemsBuffer = new(maxContextDepth);
	private readonly NList<uint> context = new(maxContextDepth), reservedContext = new(maxContextDepth);
	private readonly SumSet<uint> freqTable = [], excludingFreqTable = [];
	private SumSet<uint> outputFreqTable = [];
	private readonly NList<Interval> intervalsForBuffer = [];
	private int lzBlockEnd = 0;

	public bool Encode()
	{
		Prerequisites();
		for (var i = startPos; i < Input.Length; i++, _ = LastBlock ? Status[TN] = i : 0)
		{
			var item = Input[i][0].Lower;
			Input.GetSlice(Max(startPos, i - maxContextDepth)..i).ForEach((x, index) => context.SetOrAdd(index, x[0].Lower));
			context.Reverse();
			reservedContext.Replace(context);
			intervalsForBuffer.Clear();
			if (i >= lzBlockEnd && (context.Length != maxContextDepth || i < (maxContextDepth << 1) + startPos || !ProcessLZ(context, i) || i >= lzBlockEnd))
			{
				freqTable.Clear();
				excludingFreqTable.Clear();
				Escape(item, out var sum, out var frequency);
				ProcessFrequency(item, ref sum, ref frequency);
				ProcessBuffers(i);
			}
			var contextLength = reservedContext.Length;
			Increase(reservedContext, context, item, out var hlIndex);
			if (contextLength == maxContextDepth)
				lzBuffer.SetOrAdd((i - startPos - maxContextDepth) % LZDictionarySize, hlIndex);
		}
		while (buffer.Length != 0)
			buffer.Dequeue().ForEach(x => Result.Add(new(x.Lower, x.Length, x.Base)));
		return true;
	}

	private void Prerequisites()
	{
		EnsureValidInput();
		ValidateLastBlock();
		WriteAppliedMethods();
		ValidateFirstBlock();
		Result.WriteCount((uint)(Input.Length - startPos));
		Result.WriteCount((uint)Min(LZDictionarySize, FragmentLength));
		PrepareFields();
	}

	private void EnsureValidInput()
	{
		if (Input.Length < 4)
			throw new EncoderFallbackException();
		appliedMethods = Input[0];
		startPos = appliedMethods.Length >= 1 && appliedMethods[0] == LengthsApplied ? (int)appliedMethods[1].Base + 1 : 1;
		if (Input.Length <= startPos)
			throw new EncoderFallbackException();
		var firstActual = Input[startPos];
		if (firstActual.Length is not 1 and not 2 || firstActual[0].Length != 1)
			throw new EncoderFallbackException();
		inputBase = firstActual[0].Base;
		if (inputBase < 2 || firstActual[^1].Length != 1)
			throw new EncoderFallbackException();
		var restOfInput = Input.GetSlice(startPos + 1);
		if (!restOfInput.All(x => x.Length == Input[startPos].Length && x[0].Length == 1
			&& x[0].Base == inputBase && (x.Length == 1 || x[1].Length == 1 && x[1].Base == Input[startPos][1].Base)))
			throw new EncoderFallbackException();
	}

	private void ValidateLastBlock()
	{
		if (LastBlock)
		{
			Status[TN] = 0;
			StatusMaximum[TN] = Input.Length - startPos;
		}
	}

	private void WriteAppliedMethods()
	{
		for (var i = 0; i < appliedMethods.Length; i++)
			Result.Add(new(appliedMethods[i].Lower, appliedMethods[i].Length, appliedMethods[i].Base));
	}

	private void ValidateFirstBlock()
	{
		if (BlockIndex == 0)
		{
			Result.Add(new(Input[1][0].Lower, 1, 3));
			Result.WriteCount(inputBase);
			for (var i = 2; i < startPos; i++)
				for (var j = 0; j < Input[i].Length; j++)
					Result.Add(new(Input[i][j].Lower, Input[i][j].Length, Input[i][j].Base));
		}
	}

	private void PrepareFields()
	{
		globalFreqTable.Clear();
		if (BlockIndex == 2)
			newItemsFreqTable.Clear();
		else
			newItemsFreqTable.Replace(new Chain((int)inputBase).Convert(x => ((uint)x, 1)));
		buffer.Clear();
		comparer = BlockIndex == 2 ? new NListEComparer<uint>() : new EComparer<NList<uint>>((x, y) => x.Equals(y),
			x => unchecked(x.Progression(17 * 23 + x.Length, (x, y) => x * 23 + y.GetHashCode())));
		contextSet = new(comparer);
		lzBuffer = [];
		contextFreqTableByLevel.Clear();
		lzLengths.Replace([1]);
		lzCount = notLZCount = spaceCount = notSpaceCount = 1;
		spaceBuffer.Clear();
		newItemsBuffer.Clear();
		context.Clear();
		reservedContext.Clear();
		freqTable.Clear();
		excludingFreqTable.Clear();
		intervalsForBuffer.Clear();
		lzBlockEnd = 0;
	}

	private void Escape(uint item, out long sum, out int frequency)
	{
		for (; context.Length > 0 && !contextSet.TryGetIndexOf(context, out _); context.RemoveAt(^1)) ;
		sum = 0;
		frequency = 0;
		while (true)
		{
			if (context.Length <= 0 || !contextSet.TryGetIndexOf(context, out var index))
				break;
			freqTable.Replace(contextFreqTableByLevel[index]).ExceptWith(excludingFreqTable);
			if ((sum = freqTable.GetLeftValuesSum(item, out frequency)) < 0 || frequency != 0)
				break;
			if (freqTable.Length != 0)
				intervalsForBuffer.Add(new((uint)freqTable.ValuesSum, (uint)freqTable.Length * 100, GetFreqTableBase(freqTable)));
			context.RemoveAt(^1);
			excludingFreqTable.UnionWith(freqTable);
		}
		if (freqTable.Length == 0 || context.Length == 0)
		{
			foreach (var (Key, _) in excludingFreqTable)
				excludingFreqTable.Update(Key, globalFreqTable.TryGetValue(Key, out var newValue) ? newValue : throw new EncoderFallbackException());
			outputFreqTable = globalFreqTable.ExceptWith(excludingFreqTable);
		}
		else
			outputFreqTable = freqTable;
	}

	private void ProcessFrequency(uint item, ref long sum, ref int frequency)
	{
		if (frequency == 0)
			sum = outputFreqTable.GetLeftValuesSum(item, out frequency);
		if (frequency == 0)
		{
			if (outputFreqTable.Length != 0)
				intervalsForBuffer.Add(new((uint)outputFreqTable.ValuesSum, (uint)outputFreqTable.Length * 100, GetFreqTableBase(outputFreqTable)));
			if (BlockIndex != 2)
			{
				intervalsForBuffer.Add(new((uint)newItemsFreqTable.IndexOf(item), (uint)newItemsFreqTable.Length));
				newItemsFreqTable.RemoveValue(item);
				newItemsBuffer.Enqueue(item);
			}
		}
		else
		{
			intervalsForBuffer.Add(new(0, (uint)outputFreqTable.ValuesSum, GetFreqTableBase(outputFreqTable)));
			intervalsForBuffer.Add(new((uint)sum, (uint)frequency, (uint)outputFreqTable.ValuesSum));
			newItemsBuffer.Enqueue(uint.MaxValue);
		}
		if (freqTable.Length == 0 || context.Length == 0)
			globalFreqTable.UnionWith(excludingFreqTable);
	}

	private void ProcessBuffers(int i)
	{
		var isSpace = false;
		if (BlockIndex == 2)
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
		if (BlockIndex == 2 && spaceBuffer.IsFull)
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
		var contextIndex = contextSet.IndexOf(context);
		var indexes = lzBuffer.IndexesOf(contextIndex).Sort();
		for (var i = 0; i < indexes.Length; i++)
		{
			var pos = indexes[i];
			var dist = (pos - (curPos - startPos - maxContextDepth)) % LZDictionarySize + curPos - startPos - maxContextDepth;
			int length;
			for (length = -maxContextDepth; length < Input.Length - startPos - curPos && RedStarLinq.Equals(Input[curPos + length], Input[dist + maxContextDepth + startPos + length], (x, y) => x.Lower == y.Lower); length++) ;
			if (curPos - (dist + maxContextDepth + startPos) >= 2 && length > bestLength)
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
			Result.Add(new((uint)bestPos, (uint)Min(curPos - startPos - maxContextDepth, LZDictionarySize - 1)));
			lzPositions.Add((uint)bestPos, 100);
		}
		if (bestLength < lzLengths.Length - 1)
		{
			Result.Add(new((uint)lzLengths.GetLeftValuesSum(bestLength, out var frequency), (uint)frequency, (uint)lzLengths.ValuesSum));
			lzLengths.Increase(bestLength);
		}
		else
		{
			Result.Add(new((uint)(lzLengths.ValuesSum - lzLengths[^1]), (uint)lzLengths[^1], (uint)lzLengths.ValuesSum));
			lzLengths.Increase(lzLengths.Length - 1);
			foreach (var bit in EncodeFibonacci((uint)(bestLength - lzLengths.Length + 2)))
				Result.Add(new(bit ? 1u : 0, 2));
			new Chain(bestLength - lzLengths.Length + 1).ForEach(x => lzLengths.Insert(lzLengths.Length - 1, 1));
		}
		buffer.Clear();
		spaceBuffer.Clear();
		if (BlockIndex != 2)
			newItemsBuffer.Filter(x => x != uint.MaxValue).ForEach(x => newItemsFreqTable.Add((x, 1)));
		newItemsBuffer.Clear();
		lzBlockEnd = curPos + bestLength;
		return true;
	}

	void Increase(NList<uint> context, NList<uint> successContext, uint item, out int outIndex)
	{
		outIndex = -1;
		for (; context.Length > 0 && contextSet.TryAdd(context.Copy(), out var index); context.RemoveAt(^1))
		{
			if (outIndex == -1)
				outIndex = index;
			contextFreqTableByLevel.SetOrAdd(index, [(item, 100)]);
		}
		var successLength = context.Length;
		if (context.Length != 0)
			successContext.Replace(context).RemoveAt(^1);
		for (; context.Length > 0 && contextSet.TryGetIndexOf(context, out var index); context.RemoveAt(^1), _ = context.Length == 0 ? null : successContext.RemoveAt(^1))
		{
			if (outIndex == -1)
				outIndex = index;
			if (!contextFreqTableByLevel[index].TryGetValue(item, out var itemValue))
			{
				contextFreqTableByLevel[index].Add(item, 100);
				continue;
			}
			else if (context.Length == 1 || itemValue > 100)
			{
				contextFreqTableByLevel[index].Update(item, itemValue + (int)Max(Round((double)100 / (successLength - context.Length + 1)), 1));
				continue;
			}
			var successIndex = contextSet.IndexOf(successContext);
			if (!contextFreqTableByLevel[successIndex].TryGetValue(item, out var successValue))
				successValue = 100;
			var step = (double)(contextFreqTableByLevel[index].ValuesSum + contextFreqTableByLevel[index].Length * 100) * successValue
				/ (contextFreqTableByLevel[index].ValuesSum + contextFreqTableByLevel[successIndex].ValuesSum + contextFreqTableByLevel[successIndex].Length * 100 - successValue);
			contextFreqTableByLevel[index].Update(item, (int)(Max(Round(step), 1) + itemValue));
		}
		if (globalFreqTable.TryGetValue(item, out var globalValue))
			globalFreqTable.Update(item, globalValue + (int)Max(Round((double)100 / (successLength + 1)), 1));
		else
			globalFreqTable.Add(item, 100);
	}

	private static uint GetFreqTableBase(SumSet<uint> freqTable) => (uint)(freqTable.ValuesSum + freqTable.Length * 100);
}
