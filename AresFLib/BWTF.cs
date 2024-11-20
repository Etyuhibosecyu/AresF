using Mpir.NET;

namespace AresFLib;

internal record class BWTF(NList<ShortIntervalList> Result, int TN)
{
	public NList<ShortIntervalList> Encode(NList<ShortIntervalList> input)
	{
		if (input.Length == 0)
			throw new EncoderFallbackException();
		if (input[0].Contains(HuffmanApplied) || input[0].Contains(BWTApplied))
			return input;
		Current[TN] = 0;
		CurrentMaximum[TN] = ProgressBarStep * 2;
		Status[TN] = 0;
		StatusMaximum[TN] = 10;
		var lz = CreateVar(input[0].IndexOf(LempelZivApplied), out var lzIndex) != -1;
		var startPos = (lz ? (input[0].Length >= lzIndex + BWTBlockExtraSize && input[0][lzIndex + 1] == LempelZivSubdivided ? 3 : 2) : 1) + (input[0].Length >= 1 && input[0][0] == LengthsApplied ? (int)input[0][1].Base : 0);
		Status[TN]++;
		if (!(input.Length >= startPos + BWTBlockExtraSize && input.GetRange(startPos).All(x => x.Length == 1 && x[0].Base == ValuesInByte)))
			throw new EncoderFallbackException();
		Status[TN]++;
		Result.Replace(input.GetRange(0, startPos));
		Result[0] = new(Result[0]) { new((uint)BitsCount((uint)BWTBlockSize) - 14, 18) };
		Status[TN]++;
		var byteInput = input.GetRange(startPos).ToNList(x => (byte)x[0].Lower);
		BitList bitInput = new(byteInput.Length);
		using BitList bitInputBetweenZLE = new(BWTBlockExtraSize, false);
		Status[TN]++;
		var uniqueElems = byteInput.ToHashSet();
		Status[TN]++;
		var uniqueElems2 = uniqueElems.ToNList().Sort();
		Status[TN]++;
		var inputPos = startPos;
		Status[TN] = 0;
		StatusMaximum[TN] = byteInput.Length;
		Current[TN] += ProgressBarStep;
		var byteResult = RedStarLinq.NEmptyList<byte>(byteInput.Length + GetArrayLength(byteInput.Length, BWTBlockSize) * BWTBlockExtraSize);
		BWTInternal();
		Status[TN] = 0;
		StatusMaximum[TN] = byteResult.Length;
		Current[TN] += ProgressBarStep;
		byteInput.Clear();
		for (var i = 0; i < byteResult.Length; i += BWTBlockSize)
		{
			byteInput.AddRange(byteResult.GetRange(i..(i += BWTBlockExtraSize)));
			using var zle = ZLE(byteResult.Skip(i).Take(BWTBlockSize), byteInput.GetRange(^BWTBlockExtraSize..), uniqueElems2[0], out var zerosB);
			byteInput.AddRange(zle);
			bitInput.AddRange(bitInputBetweenZLE).AddRange(zerosB);
		}
		uniqueElems2 = byteResult.Filter((x, index) => index % (BWTBlockSize + BWTBlockExtraSize) >= BWTBlockExtraSize).ToHashSet().ToNList().Sort();
		byteResult.Dispose();
		Result.AddRange(byteInput.Combine(bitInput, (x, y) => y ? ByteIntervalsPlus1[ValuesInByte] : ByteIntervalsPlus1[x]));
		Result[0].Add(BWTApplied);
		uniqueElems.ExceptWith(uniqueElems2);
#if DEBUG
		var input2 = input.Skip(startPos);
		var decoded = new DecodingF().DecodeBWT(Result.GetRange(startPos), [.. uniqueElems], BWTBlockSize);
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
		decoded.Dispose();
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
			var buffer = RedStarLinq.FillArray(Environment.ProcessorCount, index => byteInput.Length < BWTBlockSize * (index + 1) ? default! : RedStarLinq.NEmptyList<byte>(BWTBlockSize * 2 - 1));
			var currentBlock = RedStarLinq.FillArray(buffer.Length, index => byteInput.Length < BWTBlockSize * (index + 1) ? default! : RedStarLinq.NEmptyList<byte>(BWTBlockSize));
			var indexes = RedStarLinq.FillArray(buffer.Length, index => byteInput.Length < BWTBlockSize * (index + 1) ? default! : RedStarLinq.NEmptyList<int>(BWTBlockSize));
			var tasks = new Task[buffer.Length];
			var MTFMemory = RedStarLinq.FillArray<byte[]>(buffer.Length, _ => default!);
			var multiThreadedCompare = buffer[buffer.Length * 3 / 4] == null;
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
					buffer[i % buffer.Length]?.Dispose();
					currentBlock[i % buffer.Length]?.Dispose();
					indexes[i % buffer.Length]?.Dispose();
					buffer[i % buffer.Length] = RedStarLinq.NEmptyList<byte>((byteInput.Length - i2) * 2 - 1);
					currentBlock[i % buffer.Length] = RedStarLinq.NEmptyList<byte>(byteInput.Length - i2);
					indexes[i % buffer.Length] = RedStarLinq.NEmptyList<int>(byteInput.Length - i2);
				}
				for (var j = 0; j < length; j++)
					currentBlock[i % buffer.Length][j] = byteInput[i2 + j];
				var i3 = i;
				tasks[i % buffer.Length] = Task.Factory.StartNew(() => BWTMain(i3));
			}
			tasks.ForEach(x => x?.Wait());
			buffer.ForEach(x => x?.Dispose());
			currentBlock.ForEach(x => x?.Dispose());
			indexes.ForEach(x => x?.Dispose());
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
			void GetBWT(NList<byte> source, NList<byte> buffer, NList<int> indexes, NList<byte> result, ref int firstPermutation)
			{
				buffer.SetRange(0, source);
				buffer.SetRange(source.Length, source[..^1]);
				for (var i = 0; i < indexes.Length; i++)
					indexes[i] = i;
				var indexesToSort = buffer.BWTCompare(source.Length, multiThreadedCompare ? TN : -1);
				foreach (var index in indexesToSort)
				{
					indexes.Sort(x => buffer[index + x]);
					Status[TN] += (int)Floor((double)source.Length / indexesToSort.Length);
				}
				indexesToSort.Dispose();
#if DEBUG
				if (!indexes.AllUnique())
					throw new InvalidOperationException();
#endif
				firstPermutation = indexes.IndexOf(0);
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
				}
			}
		}
	}

	private Slice<byte> ZLE(Slice<byte> input, NList<byte> firstPermutationRange, byte zero, out Slice<bool> zerosB)
	{
		var frequency = new int[ValuesInByte];
		for (var i = 0; i < input.Length; i++)
			frequency[input[i]]++;
		var zeroB = Array.IndexOf(frequency, 0);
		var preResult = new NList<short>(input.Length + 2) { zero, (short)zeroB };
		for (var i = 0; i < input.Length;)
		{
			while (i < input.Length && input[i] != zero)
			{
				preResult.Add(input[i++]);
				Status[TN]++;
			}
			if (i >= input.Length)
				break;
			var j = i;
			while (i < input.Length && input[i] == zero)
			{
				i++;
				Status[TN]++;
			}
			if (i == j)
				throw new EncoderFallbackException();
			using var toAdd = ((MpzT)(i - j + 1)).ToString(2)?.Skip(1).ToNList(x => (short)(x == '1' ? zeroB : x == '0' ? zero : throw new EncoderFallbackException()));
			if (toAdd != null)
				preResult.AddRange(toAdd);
		}
#if DEBUG
		var input2 = input;
		var pos = 0;
		var decoded = new DecodingF().DecodeZLE(preResult.GetSlice(), ref pos, BWTBlockSize);
		for (var i = 0; i < input2.Length && i < decoded.Length; i++)
		{
			var x = input2[i];
			var y = decoded[i];
			if (x != y)
				throw new DecoderFallbackException();
		}
		if (input2.Length != decoded.Length)
			throw new DecoderFallbackException();
		decoded.Dispose();
#endif
		if (preResult.Length < input.Length * 0.936)
		{
			firstPermutationRange[0] |= ValuesInByte >> 1;
			zerosB = preResult.Convert(x => x is < 0 or >= ValuesInByte);
			return preResult.Convert(x => unchecked((byte)x));
		}
		else
		{
			zerosB = new BitList(input.Length, false).GetSlice();
			return input;
		}
	}
}
