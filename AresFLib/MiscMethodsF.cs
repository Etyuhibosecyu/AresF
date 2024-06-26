
using Mpir.NET;

namespace AresFLib;

partial class Compression
{
	private BitList ArchaicHuffman(List<ShortIntervalList> input)
	{
		var bwtIndex = input[0].IndexOf(BWTApplied);
		if (CreateVar(input[0].IndexOf(HuffmanApplied), out var huffmanIndex) != -1 && !(bwtIndex != -1 && huffmanIndex == bwtIndex + 1))
			throw new EncoderFallbackException();
		Current[tn] = 0;
		CurrentMaximum[tn] = ProgressBarStep * 2;
		Status[tn] = 0;
		StatusMaximum[tn] = 3;
		var lz = CreateVar(input[0].IndexOf(LempelZivApplied), out var lzIndex) != -1 && (bwtIndex == -1 || lzIndex != bwtIndex + 1);
		var lzDummy = CreateVar(input[0].IndexOf(LempelZivDummyApplied), out var lzDummyIndex) != -1 && (bwtIndex == -1 || lzDummyIndex != bwtIndex + 1);
		var bwtLength = bwtIndex != -1 ? (int)input[0][bwtIndex + 1].Base : 0;
		var startPos = (lz || lzDummy ? (input[0].Length >= lzIndex + 2 && input[0][lzIndex + 1] == LempelZivSubdivided ? 3 : 2) : 1) + (input[0].Length >= 1 && input[0][0] == LengthsApplied ? (int)input[0][1].Base : 0) + bwtLength;
		Status[tn]++;
		var lzPos = bwtIndex != -1 ? 4 : 2;
		if (input.Length < startPos + lzPos + 1)
			throw new EncoderFallbackException();
		var originalBase = input[startPos + lzPos][0].Base;
		if (!input.GetSlice(startPos + lzPos + 1).All((x, index) => bwtIndex != -1 && (index + lzPos + 1) % (BWTBlockSize + 2) is 0 or 1 || x[0].Base == originalBase))
			throw new EncoderFallbackException();
		var frequencyTable = input.GetSlice(startPos).FrequencyTable(x => x[0].Lower).NSort(x => ~(uint)x.Count);
		var nodes = frequencyTable.ToList(x => new ArchaicHuffmanNode(x.Key, x.Count));
		var maxFrequency = nodes[0].Count;
		Current[tn] = 0;
		CurrentMaximum[tn] = ProgressBarStep * 2;
		Status[tn] = 0;
		StatusMaximum[tn] = nodes.Length - 1;
		Comparer<ArchaicHuffmanNode> comparer = new((x, y) => (~x.Count).CompareTo(~y.Count));
		var dic = frequencyTable.ToDictionary(x => x.Key, x => new BitList());
		while (nodes.Length > 1)
		{
			ArchaicHuffmanNode node = new(nodes[^1], nodes[^2]);
			nodes.Remove(^2);
			var pos = nodes.BinarySearch(node, comparer);
			if (pos < 0)
				pos = ~pos;
			nodes.Insert(pos, node);
			foreach (var x in node.Left!)
				dic[x].Add(false);
			foreach (var x in node.Right!)
				dic[x].Add(true);
			Status[tn]++;
		}
		dic.ForEach(x => x.Value.Reverse());
		BitList result = new((input.Length - startPos) * BitsPerByte);
		result.AddRange(EncodeFibonacci((uint)maxFrequency));
		result.AddRange(EncodeFibonacci((uint)frequencyTable.Length));
		Status[tn] = 0;
		StatusMaximum[tn] = frequencyTable.Length;
		Current[tn] += ProgressBarStep;
		for (var i = 0; i < frequencyTable.Length; i++, Status[tn]++)
		{
			result.AddRange(EncodeEqual(frequencyTable[i].Key, input[startPos + lzPos][0].Base));
			if (i != 0)
				result.AddRange(EncodeEqual((uint)frequencyTable[i].Count - 1, (uint)frequencyTable[i - 1].Count));
		}
		Status[tn] = 0;
		StatusMaximum[tn] = input.Length;
		Current[tn] += ProgressBarStep;
		for (var i = startPos; i < input.Length; i++, Status[tn]++)
		{
			result.AddRange(dic[input[i][0].Lower]);
			for (var j = 1; j < input[i].Length; j++)
				result.AddRange(EncodeEqual(input[i][j].Lower, input[i][j].Base));
		}
		return result;
	}

	public List<ShortIntervalList> BWT(List<ShortIntervalList> input)
	{
		if (input.Length == 0)
			throw new EncoderFallbackException();
		if (input[0].Contains(HuffmanApplied) || input[0].Contains(BWTApplied))
			return input;
		Current[tn] = 0;
		CurrentMaximum[tn] = ProgressBarStep * 2;
		var lz = CreateVar(input[0].IndexOf(LempelZivApplied), out var lzIndex) != -1;
		var startPos = (lz ? (input[0].Length >= lzIndex + BWTBlockExtraSize && input[0][lzIndex + 1] == LempelZivSubdivided ? 3 : 2) : 1) + (input[0].Length >= 1 && input[0][0] == LengthsApplied ? (int)input[0][1].Base : 0);
		if (!(input.Length >= startPos + BWTBlockExtraSize && input.GetSlice(startPos).All(x => x.Length == 1 && x[0].Base == ValuesInByte)))
			throw new EncoderFallbackException();
		result.Replace(input.GetSlice(0, startPos));
		result[0] = new(result[0]);
		Status[tn] = 0;
		StatusMaximum[tn] = 7;
		var byteInput = input.GetSlice(startPos).ToNList(x => (byte)x[0].Lower);
		Status[tn]++;
		var uniqueElems = byteInput.ToHashSet();
		Status[tn]++;
		var uniqueElems2 = uniqueElems.ToNList().Sort();
		Status[tn]++;
		var inputPos = startPos;
		NList<byte> byteResult;
		Status[tn] = 0;
		StatusMaximum[tn] = byteInput.Length;
		Current[tn] += ProgressBarStep;
		byteResult = byteInput.Copy().AddRange(new byte[GetArrayLength(byteInput.Length, BWTBlockSize) * BWTBlockExtraSize]);
		BWTInternal();
		byteInput.Clear();
		for (var i = 0; i < byteResult.Length; i += BWTBlockSize)
			byteInput.AddRange(byteResult.GetRange(i..(i += BWTBlockExtraSize))).AddRange(ZLE(byteResult.Skip(i).Take(BWTBlockSize), byteInput.GetRange(^BWTBlockExtraSize..), uniqueElems2[0]));
		uniqueElems2 = byteResult.Filter((x, index) => index % (BWTBlockSize + BWTBlockExtraSize) >= BWTBlockExtraSize).ToHashSet().ToNList().Sort();
		result.AddRange(byteInput.Convert(x => new ShortIntervalList() { new(x, ValuesInByte) }));
		result[0].Add(BWTApplied);
		uniqueElems.ExceptWith(uniqueElems2);
#if DEBUG
		var input2 = input.Skip(startPos);
		var decoded = new DecodingF().DecodeBWT(result.GetRange(startPos), [.. uniqueElems]);
		for (var i = 0; i < input2.Length && i < decoded.Length; i++)
			for (var j = 0; j < input2[i].Length && j < decoded[i].Length; j++)
			{
				var x = input2[i][j];
				var y = decoded[i][j];
				if (!(x.Equals(y) || GetBaseWithBuffer(x.Base) == y.Base && x.Lower == y.Lower && x.Length == y.Length))
					throw new DecoderFallbackException();
			}
		if (input2.Length != decoded.Length)
			throw new DecoderFallbackException();
#endif
		var c = uniqueElems.PConvert(x => new Interval(x, ValuesInByte));
		c.Insert(0, GetCountList((uint)uniqueElems.Length));
		var cSplit = c.SplitIntoEqual(8);
		c.Dispose();
		var cLength = (uint)cSplit.Length;
		result[0].Add(new(0, cLength, cLength));
		result.Insert(startPos, cSplit.PConvert(x => new ShortIntervalList(x)));
		cSplit.Dispose();
		return result;
		void BWTInternal()
		{
			var buffer = RedStarLinq.FillArray(Environment.ProcessorCount, index => byteInput.Length < BWTBlockSize * (index + 1) ? default! : new byte[BWTBlockSize * 2 - 1]);
			var currentBlock = RedStarLinq.FillArray(buffer.Length, index => byteInput.Length < BWTBlockSize * (index + 1) ? default! : new byte[BWTBlockSize]);
			var indexes = RedStarLinq.FillArray(buffer.Length, index => byteInput.Length < BWTBlockSize * (index + 1) ? default! : new int[BWTBlockSize]);
			var tasks = new Task[buffer.Length];
			var MTFMemory = RedStarLinq.FillArray<byte[]>(buffer.Length, _ => default!);
			for (var i = 0; i < GetArrayLength(byteInput.Length, BWTBlockSize); i++)
			{
				tasks[i % buffer.Length]?.Wait();
				int i2 = i * BWTBlockSize, length = Min(BWTBlockSize, byteInput.Length - i2);
				MTFMemory[i % buffer.Length] = [.. uniqueElems2];
				if (byteInput.Length - i2 < BWTBlockSize)
				{
					buffer[i % buffer.Length] = default!;
					currentBlock[i % buffer.Length] = default!;
					indexes[i % buffer.Length] = default!;
					GC.Collect();
					buffer[i % buffer.Length] = new byte[(byteInput.Length - i2) * 2 - 1];
					currentBlock[i % buffer.Length] = new byte[byteInput.Length - i2];
					indexes[i % buffer.Length] = new int[byteInput.Length - i2];
				}
				for (var j = 0; j < length; j++)
					currentBlock[i % buffer.Length][j] = byteInput[i2 + j];
				var i3 = i;
				tasks[i % buffer.Length] = Task.Factory.StartNew(() => BWTMain(i3));
			}
			tasks.ForEach(x => x?.Wait());
			void BWTMain(int blockIndex)
			{
				var firstPermutation = 0;
				//Сортировка контекстов с обнаружением, в какое место попал первый
				GetBWT(currentBlock[blockIndex % buffer.Length]!, buffer[blockIndex % buffer.Length]!, indexes[blockIndex % buffer.Length], currentBlock[blockIndex % buffer.Length]!, ref firstPermutation);
				for (var i = BWTBlockExtraSize - 1; i >= 0; i--)
				{
					byteResult[(BWTBlockSize + BWTBlockExtraSize) * blockIndex + i] = unchecked((byte)firstPermutation);
					firstPermutation >>= BitsPerByte;
				}
				WriteToMTF(blockIndex);
			}
			void GetBWT(byte[] source, byte[] buffer, int[] indexes, byte[] result, ref int firstPermutation)
			{
				CopyMemory(source, 0, buffer, 0, source.Length);
				CopyMemory(source, 0, buffer, source.Length, source.Length - 1);
				for (var i = 0; i < indexes.Length; i++)
					indexes[i] = i;
				var chainLength = buffer.BWTCompare(source.Length);
				new Chain(chainLength).ForEach(i => indexes.NSort(x => buffer[chainLength - 1 - i + x]));
#if DEBUG
				if (indexes.ToHashSet().Length != indexes.Length)
					throw new InvalidOperationException();
#endif
				firstPermutation = Array.IndexOf(indexes, 0);
				// Копирование результата
				for (var i = 0; i < source.Length; i++)
					result[i] = buffer[indexes[i] + indexes.Length - 1];
			}
			void WriteToMTF(int blockIndex)
			{
				for (var i = 0; i < currentBlock[blockIndex % buffer.Length].Length; i++)
				{
					var elem = currentBlock[blockIndex % buffer.Length][i];
					var index = Array.IndexOf(MTFMemory[blockIndex % buffer.Length]!, elem);
					byteResult[(BWTBlockSize + BWTBlockExtraSize) * blockIndex + i + BWTBlockExtraSize] = uniqueElems2[index];
					Array.Copy(MTFMemory[blockIndex % buffer.Length]!, 0, MTFMemory[blockIndex % buffer.Length]!, 1, index);
					MTFMemory[blockIndex % buffer.Length][0] = elem;
					Status[tn]++;
				}
			}
		}
	}

	private static Slice<byte> ZLE(Slice<byte> input, NList<byte> firstPermutationRange, byte zero)
	{
		var frequency = new int[ValuesInByte];
		for (var i = 0; i < input.Length; i++)
			frequency[input[i]]++;
		var zeroB = Array.IndexOf(frequency, 0);
		if (zeroB == -1)
			return input;
		var result = new NList<byte>(input.Length + 2) { zero, (byte)zeroB };
		for (var i = 0; i < input.Length;)
		{
			while (i < input.Length && input[i] != zero)
				result.Add(input[i++]);
			if (i >= input.Length)
				break;
			var j = i;
			while (i < input.Length && i - j < ValuesIn2Bytes && input[i] == zero)
				i++;
			if (i == j)
				throw new EncoderFallbackException();
			result.AddRange(((MpzT)(i - j + 1)).ToString(2)?.Skip(1).ToArray(x => (byte)(x == '1' ? zeroB : x == '0' ? zero : throw new EncoderFallbackException())));
		}
		if (result.Length < input.Length * 0.936)
		{
			firstPermutationRange[0] |= ValuesInByte >> 1;
			return result.GetSlice();
		}
		else
			return input;
	}
}
