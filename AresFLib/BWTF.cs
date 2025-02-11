﻿using Mpir.NET;

namespace AresFLib;

/// <summary>
/// Класс, выполняющий сжатие методом BWT (Burrows-Wheeler transform - преобразование Барроуза-Уилера, подробнее
/// см. <a href="https://ru.wikipedia.org/wiki/BWT">здесь</a>).<br/>
/// Использование: <tt>new PPM(input, result, tn).Encode();</tt>.<br/>
/// </summary>
/// <param name="Input">Входной поток для сжатия.</param>
/// <param name="Result">В<u>ы</u>ходной поток для сжатых данных.</param>
/// <param name="TN">Номер потока (см. <see cref="Threads"/>).</param>
/// <remarks>
/// Как привести входной поток к виду, приемлемому для этого класса, см. в проекте AresFLib в файле RootMethodsF.cs
/// в методах PreEncode() и BWTEncode().
/// </remarks>
internal record class BWTF(NList<ShortIntervalList> Input, NList<ShortIntervalList> Result, int TN)
{
	private NList<byte> byteInput = default!, uniqueElems2 = default!, byteResult = default!;
	private NList<byte>[] buffer = default!, currentBlock = default!, mtfMemory = default!;
	private NList<int>[] indexes = default!;
	private bool multiThreadedCompare;

	/// <summary>Основной метод класса. Инструкция по применению - см. в описании класса.</summary>
	public NList<ShortIntervalList> Encode()
	{
		if (Input.Length == 0)
			throw new EncoderFallbackException();
		if (Input[0].Contains(HuffmanApplied) || Input[0].Contains(BWTApplied))
			return Input;
		Current[TN] = 0;
		CurrentMaximum[TN] = ProgressBarStep * 2;
		Status[TN] = 0;
		StatusMaximum[TN] = 10;
		var lz = CreateVar(Input[0].IndexOf(LempelZivApplied), out var lzIndex) != -1;
		var startPos = (lz ? (Input[0].Length >= lzIndex + BWTBlockExtraSize && Input[0][lzIndex + 1] == LempelZivSubdivided ? 3 : 2) : 1) + (Input[0].Length >= 1 && Input[0][0] == LengthsApplied ? (int)Input[0][1].Base : 0);
		Status[TN]++;
		if (!(Input.Length >= startPos + BWTBlockExtraSize && Input.GetRange(startPos).All(x => x.Length == 1 && x[0].Base == ValuesInByte)))
			throw new EncoderFallbackException();
		Status[TN]++;
		Result.Replace(Input.GetRange(0, startPos));
		Result[0] = new(Result[0]) { new((uint)BitsCount((uint)BWTBlockSize) - 14, 18) };
		Status[TN]++;
		byteInput = Input.GetRange(startPos).ToNList(x => (byte)x[0].Lower);
		BitList bitInput = new(byteInput.Length);
		using BitList bitInputBetweenZLE = new(BWTBlockExtraSize, false);
		Status[TN]++;
		var uniqueElems = byteInput.ToHashSet();
		Status[TN]++;
		uniqueElems2 = uniqueElems.ToNList().Sort();
		Status[TN]++;
		var inputPos = startPos;
		Status[TN] = 0;
		StatusMaximum[TN] = byteInput.Length;
		Current[TN] += ProgressBarStep;
		byteResult = RedStarLinq.NEmptyList<byte>(byteInput.Length + GetArrayLength(byteInput.Length, BWTBlockSize) * BWTBlockExtraSize);
		ExecuteMainProcess();
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
		var input2 = Input.Skip(startPos);
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
		c.Insert(0, GetNumberAsIntervalList((uint)uniqueElems.Length));
		var cSplit = c.SplitIntoEqual(8);
		c.Dispose();
		var cLength = (uint)cSplit.Length;
		Result[0].Add(new(0, cLength, cLength));
		Result.Insert(startPos, cSplit.PConvert(x => new ShortIntervalList(x)));
		cSplit.Dispose();
		return Result;
	}

	private void ExecuteMainProcess()
	{
		buffer = RedStarLinq.FillArray(Environment.ProcessorCount, index => byteInput.Length < BWTBlockSize * (index + 1) ? default! : RedStarLinq.NEmptyList<byte>(BWTBlockSize * 2 - 1));
		currentBlock = RedStarLinq.FillArray(buffer.Length, index => byteInput.Length < BWTBlockSize * (index + 1) ? default! : RedStarLinq.NEmptyList<byte>(BWTBlockSize));
		indexes = RedStarLinq.FillArray(buffer.Length, index => byteInput.Length < BWTBlockSize * (index + 1) ? default! : RedStarLinq.NEmptyList<int>(BWTBlockSize));
		var tasks = new Task[buffer.Length];
		mtfMemory = RedStarLinq.FillArray<NList<byte>>(buffer.Length, _ => default!);
		multiThreadedCompare = buffer[buffer.Length * 3 / 4] == null;
		for (var i = 0; i < GetArrayLength(byteInput.Length, BWTBlockSize); i++)
		{
			tasks[i % buffer.Length]?.Wait();
			int i2 = i * BWTBlockSize, leftLength = byteInput.Length - i2, length = Min(BWTBlockSize, leftLength);
			mtfMemory[i % buffer.Length] = uniqueElems2.Copy();
			if (leftLength < BWTBlockSize)
			{
				buffer[i % buffer.Length]?.Resize(leftLength * 2 - 1);
				currentBlock[i % buffer.Length]?.Resize(leftLength);
				indexes[i % buffer.Length]?.Resize(leftLength);
				buffer[i % buffer.Length] ??= RedStarLinq.NEmptyList<byte>(leftLength * 2 - 1);
				currentBlock[i % buffer.Length] ??= RedStarLinq.NEmptyList<byte>(leftLength);
				indexes[i % buffer.Length] ??= RedStarLinq.NEmptyList<int>(leftLength);
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
		mtfMemory.ForEach(x => x?.Dispose());
	}

	private void BWTMain(int blockIndex)
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

	/// <summary>Основной метод, выполняющий группировку (см. BWTGroup) и сортировку в BWT.</summary>
	/// <param name="source">Исходный список байт.</param>
	/// <param name="buffer">Буфер для преобразования. Представляет собой исходный список байт, к которому
	/// добавлен он же без последнего элемента (формируется непосредственно в начале этого метода, в качестве параметра
	/// нужно передать лишь <see cref="NList"/>&lt;<see langword="byte"/>&gt; соответствующей длины), что позволяет получить все
	/// циклические перестановки исходного списка без построения квадратной матрицы, что позволяет иметь огромные размеры блока
	/// без невообразимых требований к памяти (иначе для сжатия блока размером <i>n</i> байт требовалось бы
	/// <i>n</i> * <i>n</i> байт памяти). Не сортируется, сортируются индексы (см. <paramref name="indexes"/>).</param>
	/// <param name="indexes">Индексы для выбора элементов результата (см. <paramref name="result"/>) из буфера
	/// (см. <paramref name="buffer"/>). Заполняются в начале метода, параметр требует лишь список нужной длины.</param>
	/// <param name="result">Результат преобразования.</param>
	/// <param name="firstPermutation">Первая перестановка (см. <see cref="BWTBlockExtraSize"/>).</param>
	private void GetBWT(NList<byte> source, NList<byte> buffer, NList<int> indexes, NList<byte> result, ref int firstPermutation)
	{
		buffer.SetRange(0, source);
		buffer.SetRange(source.Length, source[..^1]);
		for (var i = 0; i < indexes.Length; i++)
			indexes[i] = i;
		var indexesToSort = buffer.BWTGroup(source.Length, multiThreadedCompare ? TN : -1);
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

	private void WriteToMTF(int blockIndex)
	{
		for (var i = 0; i < currentBlock[blockIndex % buffer.Length].Length; i++)
		{
			var elem = currentBlock[blockIndex % buffer.Length][i];
			var index = mtfMemory[blockIndex % buffer.Length].IndexOf(elem);
			byteResult[(BWTBlockSize + BWTBlockExtraSize) * blockIndex + i + BWTBlockExtraSize] = uniqueElems2[index];
			mtfMemory[blockIndex % buffer.Length].SetRange(1, mtfMemory[blockIndex % buffer.Length][..index]);
			mtfMemory[blockIndex % buffer.Length][0] = elem;
		}
	}

	/// <summary>
	/// Метод препроцессинга, применяемый после MTF и существенно увеличивающий коэффициент сжатия Хаффманом
	/// (о Хаффмане см. <a href="https://github.com/Etyuhibosecyu/AresTools">здесь</a>, ниже списка файлов).
	/// Взят из какой-то англоязычной статьи, ссылка на которую, к сожалению, не сохранилась.
	/// Основан, вероятно, на методе z1z2, описанном в оригинальной книге Ватолина, Ратушняка и прочих
	/// (но надеюсь, что отличается).
	/// </summary>
	/// <param name="input">Срез блока BWT после обработки MTF.</param>
	/// <param name="firstPermutationRange">Диапазон байт, содержащий первую перестановку
	/// (см. <see cref="BWTBlockExtraSize"/>).</param>
	/// <param name="zero">Байт, эквивалентный нулевому индексу в MTF.</param>
	/// <param name="zerosB">Срез, содержащий бинарные флаги, является ли тот или другой элемент "zeroB" - специальным
	/// элементом, генерируемым этим методом (по итогу алфавит потока, подаваемого на сжатие Хаффману, содержит
	/// на один элемент больше, чем алфавит потока, подаваемого на сжатие BWT (в худшем случае)).</param>
	/// <returns>Срез обработанных байт.</returns>
	/// <exception cref="EncoderFallbackException"></exception>
	/// <exception cref="DecoderFallbackException"></exception>
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
