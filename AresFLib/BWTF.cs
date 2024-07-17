
using Mpir.NET;

namespace AresFLib;

internal record class BWTF(List<ShortIntervalList> Result, int TN)
{
	public List<ShortIntervalList> Encode(List<ShortIntervalList> input)
	{
		if (input.Length == 0)
			throw new EncoderFallbackException();
		if (input[0].Contains(HuffmanApplied) || input[0].Contains(BWTApplied))
			return input;
		Current[TN] = 0;
		CurrentMaximum[TN] = ProgressBarStep * 2;
		var lz = CreateVar(input[0].IndexOf(LempelZivApplied), out var lzIndex) != -1;
		var startPos = (lz ? (input[0].Length >= lzIndex + BWTBlockExtraSize && input[0][lzIndex + 1] == LempelZivSubdivided ? 3 : 2) : 1) + (input[0].Length >= 1 && input[0][0] == LengthsApplied ? (int)input[0][1].Base : 0);
		if (!(input.Length >= startPos + BWTBlockExtraSize && input.GetSlice(startPos).All(x => x.Length == 1 && x[0].Base == ValuesInByte)))
			throw new EncoderFallbackException();
		Result.Replace(input.GetSlice(0, startPos));
		Result[0] = new(Result[0]);
		Status[TN] = 0;
		StatusMaximum[TN] = 7;
		var byteInput = input.GetSlice(startPos).ToNList(x => (byte)x[0].Lower);
		Status[TN]++;
		var uniqueElems = byteInput.ToHashSet();
		Status[TN]++;
		var uniqueElems2 = uniqueElems.ToNList().Sort();
		Status[TN]++;
		var inputPos = startPos;
		NList<byte> byteResult;
		Status[TN] = 0;
		StatusMaximum[TN] = byteInput.Length;
		Current[TN] += ProgressBarStep;
		byteResult = byteInput.Copy().AddRange(GC.AllocateUninitializedArray<byte>(GetArrayLength(byteInput.Length, BWTBlockSize) * BWTBlockExtraSize));
		BWTInternal();
		byteInput.Clear();
		for (var i = 0; i < byteResult.Length; i += BWTBlockSize)
			byteInput.AddRange(byteResult.GetRange(i..(i += BWTBlockExtraSize))).AddRange(ZLE(byteResult.Skip(i).Take(BWTBlockSize), byteInput.GetRange(^BWTBlockExtraSize..), uniqueElems2[0]));
		uniqueElems2 = byteResult.Filter((x, index) => index % (BWTBlockSize + BWTBlockExtraSize) >= BWTBlockExtraSize).ToHashSet().ToNList().Sort();
		Result.AddRange(byteInput.Convert(x => new ShortIntervalList() { new(x, ValuesInByte) }));
		Result[0].Add(BWTApplied);
		uniqueElems.ExceptWith(uniqueElems2);
#if DEBUG
		var input2 = input.Skip(startPos);
		var decoded = new DecodingF().DecodeBWT(Result.GetRange(startPos), [.. uniqueElems]);
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
		Result[0].Add(new(0, cLength, cLength));
		Result.Insert(startPos, cSplit.PConvert(x => new ShortIntervalList(x)));
		cSplit.Dispose();
		return Result;
		void BWTInternal()
		{
			var buffer = RedStarLinq.FillArray(Environment.ProcessorCount, index => byteInput.Length < BWTBlockSize * (index + 1) ? default! : GC.AllocateUninitializedArray<byte>(BWTBlockSize * 2 - 1));
			var currentBlock = RedStarLinq.FillArray(buffer.Length, index => byteInput.Length < BWTBlockSize * (index + 1) ? default! : GC.AllocateUninitializedArray<byte>(BWTBlockSize));
			var indexes = RedStarLinq.FillArray(buffer.Length, index => byteInput.Length < BWTBlockSize * (index + 1) ? default! : GC.AllocateUninitializedArray<int>(BWTBlockSize));
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
					buffer[i % buffer.Length] = GC.AllocateUninitializedArray<byte>((byteInput.Length - i2) * 2 - 1);
					currentBlock[i % buffer.Length] = GC.AllocateUninitializedArray<byte>(byteInput.Length - i2);
					indexes[i % buffer.Length] = GC.AllocateUninitializedArray<int>(byteInput.Length - i2);
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
				var indexesToSort = buffer.BWTCompare(source.Length);
				indexesToSort.ForEach(i => indexes.NSort(x => buffer[i + x]));
				indexesToSort.Dispose();
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
					Status[TN]++;
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
