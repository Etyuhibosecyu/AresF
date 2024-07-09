
namespace AresFLib;

internal partial class Compression(NList<byte> originalFile, List<ShortIntervalList> input, int tn)
{
	private readonly List<ShortIntervalList> result = [];

	internal List<ShortIntervalList> PreEncode(ref int rle, out NList<byte> originalFile2)
	{
		List<ShortIntervalList> cdl;
		NList<byte> string1, string2, cstring;
		Subtotal[tn] = 0;
		SubtotalMaximum[tn] = ProgressBarStep * 11;
		cstring = originalFile;
		Subtotal[tn] += ProgressBarStep;
		string1 = new RLE(cstring, tn).Encode();
		Subtotal[tn] += ProgressBarStep;
		string2 = new RLE(cstring, tn).RLE3();
		Subtotal[tn] += ProgressBarStep;
		Current[tn] = 0;
		Status[tn] = 0;
		if (string1.Length < cstring.Length * 0.5 && string1.Length < string2.Length)
		{
			rle = 6;
			cstring = string1;
		}
		else if (string2.Length < cstring.Length * 0.5)
		{
			rle = 12;
			cstring = string2;
		}
		originalFile2 = cstring;
		cdl = new ShortIntervalList[originalFile2.Length + 1];
		cdl[0] = [RepeatsNotApplied];
		var originalFile2_ = originalFile2;
		Parallel.For(0, originalFile2.Length, i => cdl[i + 1] = ByteIntervals[originalFile2_[i]]);
		Subtotal[tn] += ProgressBarStep;
		return cdl;
	}

	internal void Encode1(ref byte[] cs, ref int hf, ref int lz)
	{
		byte[] s;
		List<ShortIntervalList> dl1, cdl = input;
		LZData lzData = new();
		if ((PresentMethodsF & UsedMethodsF.LZ1) != 0)
		{
			dl1 = new LempelZiv(cdl, result, tn).Encode(out lzData);
			Subtotal[tn] += ProgressBarStep;
			s = WorkUpDoubleList(dl1, tn);
		}
		else
		{
			Subtotal[tn] += ProgressBarStep;
			dl1 = cdl;
			s = cs;
		}
		Subtotal[tn] += ProgressBarStep;
		if (s.Length < cs.Length && s.Length > 0)
		{
			lz = 1;
			cdl = dl1;
			cs = s;
		}
		if ((PresentMethodsF & UsedMethodsF.HF1) != 0)
			s = new AdaptiveHuffmanF(tn).Encode(cdl, lzData);
		Subtotal[tn] += ProgressBarStep;
		if (s.Length < cs.Length && s.Length > 0)
		{
			hf = 2;
			cs = s;
		}
	}

	internal void Encode2(ref byte[] cs, ref int hf)
	{
		byte[] s;
		List<ShortIntervalList> dl1, cdl = input;
		Subtotal[tn] = 0;
		SubtotalMaximum[tn] = ProgressBarStep * 4;
		Subtotal[tn] += ProgressBarStep;
		dl1 = new(BWT(cdl));
		Subtotal[tn] += ProgressBarStep;
		if ((PresentMethodsF & UsedMethodsF.AHF2) != 0)
		{
			s = new AdaptiveHuffmanF(tn).Encode(dl1, new());
			Subtotal[tn] += ProgressBarStep;
		}
		else
		{
			dl1 = new(new Huffman(dl1, result, tn).Encode());
			Subtotal[tn] += ProgressBarStep;
			s = WorkUpDoubleList(dl1, tn);
		}
		Subtotal[tn] += ProgressBarStep;
		if (s.Length < cs.Length && s.Length > 0)
		{
			hf = (PresentMethodsF & UsedMethodsF.AHF2) != 0 ? 4 : 5;
			cs = s;
		}
	}

	internal void Encode3(ref byte[] cs, ref int indicator)
	{
		byte[] s;
		Subtotal[tn] = 0;
		SubtotalMaximum[tn] = ProgressBarStep * 4;
		ArchaicHuffman(input);
		s = new LZMA(tn).Encode(input);
		Subtotal[tn] += ProgressBarStep;
		if (s.Length < originalFile.Length && s.Length > 0)
		{
			indicator = 18;
			cs = s;
		}
	}

	internal void Encode4(ref byte[] cs, ref int indicator)
	{
		byte[] s;
		Subtotal[tn] = 0;
		SubtotalMaximum[tn] = ProgressBarStep * 2;
		var ppm = new PPM(tn);
		var input2 = input.GetSlice(1).SplitIntoEqual(16000000);
		input2[0].Insert(0, input[0]);
		if (input2.Length > 1)
			throw new EncoderFallbackException();
		s = ppm.Encode(input);
		ppm.Dispose();
		Subtotal[tn] += ProgressBarStep;
		if (s.Length < originalFile.Length && s.Length > 0)
		{
			indicator = 19;
			cs = s;
		}
	}
}

public record class Executions(byte[] OriginalFile)
{
	private readonly byte[][] s = RedStarLinq.FillArray(ProgressBarGroups, _ => OriginalFile);
	private byte[] cs = OriginalFile;
	private int hf = 0, bwt = 0, rle = 0, lz = 0, misc = 0, hfP1 = 0, lzP1 = 0, hfP2 = 0, miscP3 = 0, miscP4 = 0;

	public byte[] Encode()
	{
		Total = 0;
		TotalMaximum = ProgressBarStep * 6;
		var mainInput = new Compression(OriginalFile.ToNList(), [], 0).PreEncode(ref rle, out var originalFile2);
		Total += ProgressBarStep;
		InitThreads(mainInput, originalFile2);
		ProcessThreads();
		if ((PresentMethodsF & UsedMethodsF.CS4) != 0 && s[3].Length < cs.Length && s[3].Length > 0 && s.GetSlice(0, 3).All(x => s[3].Length < x.Length))
		{
			misc = miscP4;
			cs = s[3];
		}
		else if ((PresentMethodsF & UsedMethodsF.CS3) != 0 && s[2].Length < cs.Length && s[2].Length > 0 && s[2].Length < s[1].Length && s[2].Length < s[0].Length)
		{
			misc = miscP3;
			cs = s[2];
		}
		else if ((PresentMethodsF & UsedMethodsF.CS2) != 0 && s[1].Length < cs.Length && s[1].Length > 0 && s[1].Length < s[0].Length)
		{
			hf = hfP2;
			bwt = 0;
			cs = s[1];
		}
		else if ((PresentMethodsF & UsedMethodsF.CS1) != 0 && s[0].Length < cs.Length && s[0].Length > 0)
		{
			hf = hfP1;
			bwt = 0;
			lz = lzP1;
			cs = s[0];
		}
		else
			return [(byte)rle, .. originalFile2];
		var compressedFile = new[] { (byte)(misc + lz + bwt + rle + hf) }.Concat(cs).ToArray();
#if DEBUG
		Validate(compressedFile);
#endif
		return compressedFile;
	}

	private void InitThreads(List<ShortIntervalList> mainInput, NList<byte> originalFile2)
	{
		Threads[0] = new Thread(() =>
		{
			try
			{
				if ((PresentMethodsF & UsedMethodsF.CS1) != 0)
					new Compression(originalFile2, mainInput, 0).Encode1(ref s[0], ref hfP1, ref lzP1);
			}
			catch
			{
			}
			Total += ProgressBarStep;
		});
		Threads[1] = new Thread(() =>
		{
			try
			{
				if ((PresentMethodsF & UsedMethodsF.CS2) != 0)
					new Compression(originalFile2, mainInput, 1).Encode2(ref s[1], ref hfP2);
			}
			catch
			{
			}
			Total += ProgressBarStep;
		});
		Threads[2] = new Thread(() =>
		{
			try
			{
				if ((PresentMethodsF & UsedMethodsF.CS3) != 0)
					new Compression(originalFile2, mainInput, 2).Encode3(ref s[2], ref miscP3);
			}
			catch
			{
			}
			Total += ProgressBarStep;
		});
		Threads[3] = new Thread(() =>
		{
			try
			{
				if ((PresentMethodsF & UsedMethodsF.CS4) != 0)
					new Compression(originalFile2, mainInput, 3).Encode4(ref s[3], ref miscP4);
			}
			catch
			{
			}
			Total += ProgressBarStep;
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

	private void Validate(byte[] compressedFile)
	{
		try
		{
			var decoded = new DecodingF().Decode(compressedFile, ProgramVersion);
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
