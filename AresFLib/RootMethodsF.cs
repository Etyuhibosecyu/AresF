namespace AresFLib;

internal partial class Compression(NList<byte> originalFile, NList<ShortIntervalList> input, int tn)
{
	private readonly NList<ShortIntervalList> result = [];

	/// <summary>
	/// Метод предварительного кодирования входного потока. Кодирует <see cref="RLE"/> и преобразует в двойной список
	/// (см. <a href="https://github.com/Etyuhibosecyu/AresTools">здесь</a>, ниже списка файлов).
	/// </summary>
	/// <param name="rle">Индикатор примененного <see cref="RLE"/>. Может принимать разные значения, но в каждый момент времени
	/// общий индикатор "чем сжато" равен сумме индикаторов методов.</param>
	/// <param name="originalFile2">Исходный файл, сжатый <see cref="RLE"/>, но не преобразованный в двойной список.</param>
	/// <returns>Исходный файл, сжатый <see cref="RLE"/> и преобразованный в двойной список.</returns>
	internal NList<ShortIntervalList> PreEncode(ref int rle, out NList<byte> originalFile2)
	{
		NList<byte> string1 = default!, string2, string3, cstring;
		Methods[tn] = 0;
		MethodsMaximum[tn] = ProgressBarStep * 11;
		cstring = originalFile;
		Methods[tn] += ProgressBarStep;
		var task = Task.Factory.StartNew(() => string1 = new RLE(cstring, tn).Encode());
		string2 = new RLE3(cstring, tn).Encode(false);
		string3 = new RLEMixed(cstring, tn).Encode();
		Methods[tn] += ProgressBarStep;
		task.Wait();
		Methods[tn] += ProgressBarStep;
		Current[tn] = 0;
		Status[tn] = 0;
		if (string1.Length < cstring.Length * 0.5 && string1.Length < string2.Length && string1.Length < string3.Length)
		{
			rle = 6;
			cstring = string1;
		}
		else if (string2.Length < cstring.Length * 0.5 && string2.Length < string3.Length)
		{
			rle = 12;
			cstring = string2;
		}
		else if (string3.Length < cstring.Length * 0.936)
		{
			rle = 18;
			cstring = string3;
		}
		originalFile2 = cstring;
		Methods[tn] += ProgressBarStep;
		Current[tn] = 0;
		Status[tn] = 0;
		return originalFile2.ToNList(x => ByteIntervals[x]).Insert(0, new ShortIntervalList());
	}

	/// <summary>
	/// Ветка сжатия произвольных файлов с <a href="https://ru.wikipedia.org/wiki/BWT">BWT</a> в "главной роли".
	/// Также выполняет сжатие статическим или адаптивным Хаффманом
	/// (см. <a href="https://github.com/Etyuhibosecyu/AresTools">здесь</a>, ниже списка файлов).
	/// </summary>
	/// <param name="cs">Локально вЫходной поток.</param>
	/// <param name="hf">Индикатор примененного Хаффмана. Может принимать разные значения, но в каждый момент времени
	/// общий индикатор "чем сжато" равен сумме индикаторов методов.</param>
	internal void BWTEncode(ref NList<byte> cs, ref int hf)
	{
		NList<byte> s;
		NList<ShortIntervalList> dl1, cdl = input;
		Methods[tn] = 0;
		MethodsMaximum[tn] = ProgressBarStep * 4;
		Methods[tn] += ProgressBarStep;
		dl1 = new(new BWTF(cdl, result, tn).Encode());
		Methods[tn] += ProgressBarStep;
		if ((PresentMethodsF & UsedMethodsF.AHF1) != 0)
		{
			s = new AdaptiveHuffman(tn).Encode(dl1, new());
			Methods[tn] += ProgressBarStep;
		}
		else
		{
			dl1 = new Huffman(dl1, new(dl1), tn).Encode();
			Methods[tn] += ProgressBarStep;
			s = WorkUpDoubleList(dl1, tn);
		}
		Methods[tn] += ProgressBarStep;
		if (s.Length < cs.Length && s.Length > 0)
		{
			hf = (PresentMethodsF & UsedMethodsF.AHF1) != 0 ? 4 : 5;
			cs = s;
		}
	}

	/// <summary>
	/// Ветка сжатия произвольных файлов с <a href="https://ru.wikipedia.org/wiki/LZ77">
	/// Лемпелем-Зивом</a> в "главной роли". Также выполняет сжатие адаптивным Хаффманом
	/// (см. <a href="https://github.com/Etyuhibosecyu/AresTools">здесь</a>, ниже списка файлов).
	/// </summary>
	/// <param name="cs">Локально вЫходной поток.</param>
	/// <param name="hf">Индикатор примененного Хаффмана. Может принимать разные значения, но в каждый момент времени
	/// общий индикатор "чем сжато" равен сумме индикаторов методов.</param>
	/// <param name="lz">Индикатор примененного Лемпеля-Зива.</param>
	internal void LZEncode(ref NList<byte> cs, ref int hf, ref int lz)
	{
		NList<byte> s;
		NList<ShortIntervalList> dl1, cdl = input;
		LZData lzData = new();
		if ((PresentMethodsF & UsedMethodsF.LZ2) != 0)
		{
			dl1 = new LempelZiv(cdl, result, tn).Encode();
			Methods[tn] += ProgressBarStep;
			s = WorkUpDoubleList(dl1, tn);
		}
		else
		{
			Methods[tn] += ProgressBarStep;
			dl1 = cdl;
			s = cs;
		}
		Methods[tn] += ProgressBarStep;
		if (s.Length < cs.Length && s.Length > 0)
		{
			lz = 1;
			cdl = dl1;
			cs = s;
		}
		if ((PresentMethodsF & UsedMethodsF.HF2) != 0)
			s = new AdaptiveHuffman(tn).Encode(cdl, lzData);
		Methods[tn] += ProgressBarStep;
		if (s.Length < cs.Length && s.Length > 0)
		{
			hf = 2;
			cs = s;
		}
	}

	/// <summary>
	/// Ветка сжатия произвольных файлов методом <a href="https://ru.wikipedia.org/wiki/LZMA">LZMA</a>.
	/// </summary>
	/// <param name="cs">Локально вЫходной поток.</param>
	internal void LZMAEncode(ref NList<byte> cs)
	{
		NList<byte> s;
		Methods[tn] = 0;
		MethodsMaximum[tn] = ProgressBarStep * 4;
		new ArchaicHuffman(tn).Encode(input);
		s = new LZMA(tn).Encode(input);
		Methods[tn] += ProgressBarStep;
		if (s.Length < originalFile.Length && s.Length > 0)
			cs = s;
	}

	/// <summary>
	/// Ветка сжатия произвольных файлов с
	/// <a href="https://ru.wikipedia.org/wiki/Алгоритм_сжатия_PPM">PPM</a> в "главной роли".
	/// Также выполняет разбиение на блоки, каждый из которых обработчик <see cref="PPM"/> может обработать.
	/// </summary>
	/// <param name="cs">Локально вЫходной поток.</param>
	/// <exception cref="EncoderFallbackException"/>
	internal void PPMEncode(ref NList<byte> cs)
	{
		NList<byte> s;
		Methods[tn] = 0;
		MethodsMaximum[tn] = ProgressBarStep * 2;
		var input2 = input.GetRange(1).NSplitIntoEqual(16000000).PToArray();
		input2.ForEach(x => x.Insert(0, input[0]));
		if (input2.Length < 1)
			throw new EncoderFallbackException();
		var ppm = new PPM(input2, tn);
		s = ppm.Encode(false);
		input2.ForEach(x => x?.Dispose());
		ppm.Dispose();
		Methods[tn] += ProgressBarStep;
		if (s.Length < originalFile.Length && s.Length > 0)
			cs = s;
	}
}

/// <summary>
/// Класс кодирования фрагмента произвольного файла (см. <see cref="FragmentLength"/>).<br/>
/// Использование: <tt>new FragmentEncF(OriginalFile).Encode();</tt>.<br/>
/// Если вы получили файл в виде массива байт (<tt><see langword="byte"/>[]</tt> или
/// <tt><see cref="ReadOnlySpan"/>&lt;<see langword="byte"/>&gt;</tt>),
/// перед передачей в этот метод вызовите <tt>.ToNList()</tt>.
/// </summary>
/// <param name="OriginalFile">Исходный файл (в виде нативного списка байт).</param>
/// <returns>Основной метод возвращает закодированный фрагмент.</returns>
public record class FragmentEncF(NList<byte> OriginalFile)
{
	private readonly NList<byte>[] s = RedStarLinq.FillArray(ProgressBarGroups, _ => OriginalFile);
	private NList<byte> cs = OriginalFile;
	private int hf = 0, bwt = 0, rle = 0, lz = 0, misc = 0, hfP1 = 0, hfP2 = 0, lzP2 = 0;

	/// <summary>
	/// Основной метод класса. Инструкция по применению - см. в описании класса.
	/// </summary>
	public NList<byte> Encode()
	{
		Branches = 0;
		BranchesMaximum = ProgressBarStep * 6;
		using var mainInput = new Compression(OriginalFile.ToNList(), [], 0).PreEncode(ref rle, out var originalFile2);
		Branches += ProgressBarStep;
		InitThreads(mainInput, originalFile2);
		ProcessThreads();
		if ((PresentMethodsF & UsedMethodsF.CS4) != 0 && s[3].Length < cs.Length && s[3].Length > 0 && s.GetSlice(0, 3).All(x => s[3].Length < x.Length))
		{
			misc = PPMThreshold + 1;
			cs = s[3];
		}
		else if ((PresentMethodsF & UsedMethodsF.CS3) != 0 && s[2].Length < cs.Length && s[2].Length > 0 && s[2].Length < s[1].Length && s[2].Length < s[0].Length)
		{
			misc = PPMThreshold;
			cs = s[2];
		}
		else if ((PresentMethodsF & UsedMethodsF.CS2) != 0 && s[1].Length < cs.Length && s[1].Length > 0 && s[1].Length < s[0].Length)
		{
			hf = hfP2;
			bwt = 0;
			lz = lzP2;
			cs = s[1];
		}
		else if ((PresentMethodsF & UsedMethodsF.CS1) != 0 && s[0].Length < cs.Length && s[0].Length > 0)
		{
			hf = hfP1;
			bwt = 0;
			cs = s[0];
		}
		else
			return [(byte)rle, .. originalFile2];
		var compressedFile = new[] { (byte)(misc + lz + bwt + rle + hf) }.Concat(cs).ToNList();
#if DEBUG
		Validate(compressedFile);
#endif
		return compressedFile;
	}

	private void InitThreads(NList<ShortIntervalList> mainInput, NList<byte> originalFile2)
	{
		Threads[0] = new Thread(() =>
		{
			try
			{
				if ((PresentMethodsF & UsedMethodsF.CS1) != 0)
					new Compression(originalFile2, mainInput, 0).BWTEncode(ref s[0], ref hfP1);
			}
			catch
			{
			}
			Branches += ProgressBarStep;
		});
		Threads[1] = new Thread(() =>
		{
			try
			{
				if ((PresentMethodsF & UsedMethodsF.CS2) != 0)
					new Compression(originalFile2, mainInput, 1).LZEncode(ref s[1], ref hfP2, ref lzP2);
			}
			catch
			{
			}
			Branches += ProgressBarStep;
		});
		Threads[2] = new Thread(() =>
		{
			try
			{
				if ((PresentMethodsF & UsedMethodsF.CS3) != 0)
					new Compression(originalFile2, mainInput, 2).LZMAEncode(ref s[2]);
			}
			catch
			{
			}
			Branches += ProgressBarStep;
		});
		Threads[3] = new Thread(() =>
		{
			try
			{
				if ((PresentMethodsF & UsedMethodsF.CS4) != 0)
					new Compression(originalFile2, mainInput, 3).PPMEncode(ref s[3]);
			}
			catch
			{
			}
			Branches += ProgressBarStep;
		});
		for (var i = 0; i < ProgressBarGroups; i++)
			if (Threads[i] != null && Threads[i].ThreadState is not System.Threading.ThreadState.Unstarted or System.Threading.ThreadState.Running)
				Threads[i] = default!;
	}

	private static void ProcessThreads()
	{
		Threads[0].Name = "Процесс классического сжатия";
		Threads[1].Name = "Процесс сжатия с BWT";
		Threads[2].Name = "Процесс сжатия LZMA";
		Threads[3].Name = "Процесс сжатия PPM";
		Threads.ForEach(x => _ = x == null || (x.IsBackground = true));
		Thread.CurrentThread.Priority = ThreadPriority.Lowest;
		Threads.ForEach(x => x?.Start());
		Threads.ForEach(x => x?.Join());
	}
#if DEBUG

	private void Validate(NList<byte> compressedFile)
	{
		try
		{
			using var dec = new DecodingF();
			using var decoded = dec.Decode(compressedFile, ProgramVersion);
			for (var i = 0; i < OriginalFile.Length; i++)
				if (OriginalFile[i] != decoded[i])
					throw new DecoderFallbackException();
		}
		catch (Exception ex) when (ex is not DecoderFallbackException)
		{
			throw new DecoderFallbackException();
		}
	}
#endif
}
