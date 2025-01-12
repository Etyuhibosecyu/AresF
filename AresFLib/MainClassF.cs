using System.Net;
using System.Net.Sockets;

namespace AresFLib;

public static class MainClassF
{
	private static TcpClient? client;
	private static NetworkStream? netStream;
	private static byte[] toSend = [];
	private static Thread thread = new(() => { });
	private static readonly int[] FibonacciSequence = [1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233, 377, 610, 987, 1597, 2584, 4181, 6765, 10946, 17711, 28657, 46368, 75025, 121393, 196418, 317811, 514229, 832040, 1346269, 2178309, 3524578, 5702887, 9227465, 14930352, 24157817, 39088169, 63245986, 102334155, 165580141, 267914296, 433494437, 701408733, 1134903170, 1836311903];
	private static int fragmentCount;
	private static readonly bool continue_ = true;
	private static bool isWorking;
	private static readonly object lockObj = new();

	public static void Main(string[] args)
	{
#if !RELEASE
		args = ["11000"];
		Thread.Sleep(MillisecondsPerSecond * 2);
#endif
		if (!(args.Length != 0 && int.TryParse(args[0], out var port) && port >= 1024 && port <= 65535))
			return;
		if (args.Length >= 2 && int.TryParse(args[1], out var mainProcessId))
		{
			var mainProcess = Process.GetProcessById(mainProcessId);
			mainProcess.EnableRaisingEvents = true;
			mainProcess.Exited += (_, _) => Environment.Exit(0);
		}
		Connect(port);
	}

	private static void Connect(int port)
	{
		IPEndPoint ipe = new(IPAddress.Loopback, port); //IP с номером порта
		client = new(); //подключение клиента
		try
		{
			client.Connect(ipe);
			netStream = client.GetStream();
			Thread receiveThread = new(ReceiveData) { IsBackground = true, Name = "Подключение-F" };//получение данных
			receiveThread.Start();//старт потока
		}
		catch
		{
			return;
		}
		while (true)
			SendMessage();
	}

	private static void SendMessage()
	{
		try
		{
			Thread.Sleep(MillisecondsPerSecond / 4);
			if (netStream == null)
				Disconnect();
			else if (toSend.Length != 0)
			{
				var toSendLen = BitConverter.GetBytes(toSend.Length);
				netStream.Write(toSendLen);
				netStream.Write(toSend);
				netStream.Flush(); //удаление данных из потока
				toSend = [];
			}
		}
		catch
		{
			Disconnect();
		}
	}

	private static void ReceiveData()
	{
		var receiveLen = GC.AllocateUninitializedArray<byte>(4);
		byte[] receiveMessage;
		while (true)
		{
			try
			{
				if (netStream != null)
				{
					netStream.ReadExactly(receiveLen);//чтение сообщения
					receiveMessage = GC.AllocateUninitializedArray<byte>(BitConverter.ToInt32(receiveLen));
					netStream.ReadExactly(receiveMessage);
					WorkUpReceiveMessage(receiveMessage);
				}
				else
					Disconnect();
			}
			catch
			{
				Disconnect();
			}
		}
	}

	private static void WorkUpReceiveMessage(byte[] message)
	{
		try
		{
			if (message[0] == 0)
				PresentMethodsF = (UsedMethodsF)BitConverter.ToInt32(message.AsSpan(1..));
			else if (message[0] == 1)
			{
				FragmentLength = 1000000 << Min(BitConverter.ToInt32(message.AsSpan(1..)) & 0xF, 11);
				BWTBlockSize = new[] { 50000, 100000, 200000, 500000, 1000000, 2000000, 4000000, 16000000 }[Min((BitConverter.ToInt32(message.AsSpan(1..)) & 0x70) >> 4, 7)];
				LZDictionarySize = new uint[] { 32767, 262143, 1048575, 4194303, 16777215, (1 << 26) - 1, (1 << 28) - 1, 2048000000 }[Min((BitConverter.ToInt32(message.AsSpan(1..)) & 0x380) >> 7, 7)];
			}
			else if (message[0] <= 4)
			{
				var filename = Encoding.UTF8.GetString(message[1..]);
				Files = 0;
				FilesMaximum = 0;
				thread = new((message[0] - 2) switch
				{
					0 => () => MainThread(filename, (Environment.GetEnvironmentVariable("temp") ?? throw new IOException()) + "/" + Path.GetFileNameWithoutExtension(filename), Decompress),
					1 => () => MainThread(filename, filename + ".ares-f", Compress),
					2 => () => MainThread(filename, Path.GetDirectoryName(filename) + "/" + Path.GetFileNameWithoutExtension(filename), Decompress),
					3 => () => MainThread(filename, filename, Recompress),
					_ => throw new NotImplementedException(),
				})
				{ IsBackground = true, Name = "Основной процесс" };
				thread.Start();
				Thread thread2 = new(TransferProgress) { IsBackground = true, Name = "Передача прогресса" };
				thread2.Start();
			}
		}
		catch
		{
			lock (lockObj)
			{
				isWorking = false;
				toSend = [2];
				SendMessage();
			}
		}
	}

	public static void MainThread(string filename, string filename2, Action<FileStream, FileStream> action, bool send = true)
	{
		FileStream? rfs = null, wfs = null;
		var tempFilename = "";
		try
		{
			Fragments = 0;
			isWorking = true;
			rfs = File.OpenRead(filename);
			tempFilename = (Environment.GetEnvironmentVariable("temp") ?? throw new IOException()) + "/Ares-" + Environment.ProcessId + ".tmp";
			wfs = File.Open(tempFilename, FileMode.Create);
			action(rfs, wfs);
			rfs.Close();
			wfs.Close();
			File.Move(tempFilename, filename2, true);
			lock (lockObj)
			{
				isWorking = false;
				toSend = [1];
				if (send)
					SendMessage();
			}
		}
		catch (DecoderFallbackException)
		{
			lock (lockObj)
			{
				isWorking = false;
				toSend = [(byte)(action == Compress ? 3 : 2)];
				if (send)
					SendMessage();
				else
					throw;
			}
		}
		catch
		{
			lock (lockObj)
			{
				isWorking = false;
				toSend = [2];
				if (send)
					SendMessage();
				else
					throw;
			}
		}
		finally
		{
			if (File.Exists(tempFilename))
			{
				rfs?.Close();
				wfs?.Close();
				File.Delete(tempFilename);
			}
		}
	}

	private static void TransferProgress()
	{
		Thread.Sleep(MillisecondsPerSecond);
		while (isWorking)
		{
			NList<byte> list =
			[
				0,
				.. BitConverter.GetBytes(Files),
				.. BitConverter.GetBytes(FilesMaximum),
				.. BitConverter.GetBytes(Fragments),
				.. BitConverter.GetBytes(FragmentsMaximum),
				.. BitConverter.GetBytes(Branches),
				.. BitConverter.GetBytes(BranchesMaximum),
			];
			for (var i = 0; i < ProgressBarGroups; i++)
			{
				list.AddRange(BitConverter.GetBytes(Methods[i]));
				list.AddRange(BitConverter.GetBytes(MethodsMaximum[i]));
				list.AddRange(BitConverter.GetBytes(Current[i]));
				list.AddRange(BitConverter.GetBytes(CurrentMaximum[i]));
				list.AddRange(BitConverter.GetBytes(Status[i]));
				list.AddRange(BitConverter.GetBytes(StatusMaximum[i]));
			}
			lock (lockObj)
				toSend = [.. list];
			Thread.Sleep(MillisecondsPerSecond);
		}
	}

	public static void Disconnect()
	{
		client?.Close();//отключение клиента
		netStream?.Close();//отключение потока
		var tempFilename = (Environment.GetEnvironmentVariable("temp") ?? throw new IOException()) + "/Ares-" + Environment.ProcessId + ".tmp";
		if (File.Exists(tempFilename))
			File.Delete(tempFilename);
		Environment.Exit(0); //завершение процесса
	}

	public static void Compress(Stream rfs, Stream wfs)
	{
		var oldPos = rfs.Position;
		fragmentCount = (int)Min((rfs.Length - oldPos + FragmentLength - 1) / FragmentLength, int.MaxValue / 10);
		using NList<byte> bytes = [];
		if (continue_ && fragmentCount != 0)
		{
			Fragments = 0;
			FragmentsMaximum = fragmentCount * ProgressBarStep;
			var fragmentCount2 = fragmentCount;
			BitList bits = default!;
			int i;
			for (i = FibonacciSequence.Length - 1; i >= 0; i--)
			{
				if (FibonacciSequence[i] <= fragmentCount2)
				{
					bits = new(i + 2, false) { [i] = true, [i + 1] = true };
					fragmentCount2 -= FibonacciSequence[i];
					break;
				}
			}
			for (i--; i >= 0;)
			{
				if (FibonacciSequence[i] <= fragmentCount2)
				{
					bits[i] = true;
					fragmentCount2 -= FibonacciSequence[i];
					i -= 2;
				}
				else
					i--;
			}
			bits.Insert(0, new BitList(6, ProgramVersion));
			var sizeBytes = GC.AllocateUninitializedArray<byte>((bits.Length + 7) / 8);
			bits.CopyTo(sizeBytes, 0);
			wfs.Write(sizeBytes);
		}
		if (fragmentCount > 1)
			bytes.Resize(FragmentLength);
		for (; fragmentCount > 0; fragmentCount--)
		{
			if (fragmentCount == 1)
			{
				var leftLength = (int)((rfs.Length - oldPos) % FragmentLength);
				if (leftLength != 0)
					bytes.Resize(leftLength);
			}
			rfs.ReadExactly(bytes.AsSpan());
			var s = new FragmentEncF(bytes).Encode();
			if (fragmentCount != 1)
			{
				wfs.Write(s.Length < 16000000 ? [(byte)(s.Length >> (BitsPerByte << 1)),
					unchecked((byte)(s.Length >> BitsPerByte)), unchecked((byte)s.Length)]
					: [255, 255, 255, (byte)(s.Length >> BitsPerByte * 3), unchecked((byte)(s.Length >> (BitsPerByte << 1))),
					unchecked((byte)(s.Length >> BitsPerByte)), unchecked((byte)s.Length)]);
			}
			wfs.Write(s.AsSpan());
			s.Dispose();
			Fragments += ProgressBarStep;
			GC.Collect();
		}
		if (wfs.Position >= rfs.Length - oldPos)
		{
			wfs.Seek(0, SeekOrigin.Begin);
			wfs.Write([0]);
			rfs.Seek(oldPos, SeekOrigin.Begin);
			if (rfs.Length - oldPos >= FragmentLength)
				bytes.Resize(FragmentLength);
			for (var i = 0; i < rfs.Length - oldPos; i += FragmentLength)
			{
				var length = (int)Min(rfs.Length - oldPos - i, FragmentLength);
				if (length < FragmentLength)
					bytes.Resize(length);
				rfs.ReadExactly(bytes.AsSpan());
				wfs.Write(bytes.AsSpan());
			}
			wfs.SetLength(wfs.Position);
		}
	}

	public static void Decompress(Stream rfs, Stream wfs)
	{
		PreservedFragmentLength = FragmentLength;
		FragmentLength = 2048000000;
		var readByte = (byte)rfs.ReadByte();
		var encodingVersion = (byte)(readByte & 63);
		if (encodingVersion == 0)
		{
			var bytes2 = rfs.Length < 2048000000 ? default! : GC.AllocateUninitializedArray<byte>(2048000000);
			for (var i = 1; i < rfs.Length; i += 2048000000)
			{
				var length = (int)Min(rfs.Length - i, 2048000000);
				if (length < 2048000000)
					bytes2 = GC.AllocateUninitializedArray<byte>(length);
				rfs.ReadExactly(bytes2);
				wfs.Write(bytes2);
			}
			wfs.SetLength(wfs.Position);
			return;
		}
		if (continue_)
		{
			fragmentCount = 0;
			DecodeFibonacci(rfs, readByte);
			FragmentsMaximum = fragmentCount * ProgressBarStep;
		}
		using NList<byte> bytes = [], sizeBytes = RedStarLinq.NEmptyList<byte>(4);
		for (; fragmentCount > 0; fragmentCount--)
		{
			if (fragmentCount == 1)
			{
				bytes.Resize((int)Min(rfs.Length - rfs.Position, 2048000002));
				rfs.ReadExactly(bytes.AsSpan());
			}
			else
			{
				rfs.ReadExactly(sizeBytes.AsSpan(0, 3));
				var fragmentLength = Min(sizeBytes[0] * ValuesIn2Bytes + sizeBytes[1] * ValuesInByte + sizeBytes[2], 2048000002);
				if (fragmentLength > 16000010)
				{
					rfs.ReadExactly(sizeBytes.AsSpan());
					fragmentLength = Min(sizeBytes[0] * ValuesIn3Bytes + sizeBytes[1] * ValuesIn2Bytes + sizeBytes[2] * ValuesInByte + sizeBytes[3], 2048000002);
				}
				bytes.Resize(fragmentLength);
				rfs.ReadExactly(bytes.AsSpan());
			}

			var dec = new DecodingF();
			var s = dec.Decode(bytes, encodingVersion);
			dec.Dispose();
			wfs.Write(s.AsSpan());
			s.Dispose();
			Fragments += ProgressBarStep;
			GC.Collect();
		}
		FragmentLength = PreservedFragmentLength;
	}

	private static void Recompress(Stream rfs, Stream wfs)
	{
		PreservedFragmentLength = FragmentLength;
		FragmentLength = 2048000000;
		var readByte = (byte)rfs.ReadByte();
		var encodingVersion = (byte)(readByte & 63);
		if (encodingVersion >= ProgramVersion)
			wfs.WriteByte(readByte);
#pragma warning disable CS8794 // Входные данные всегда соответствуют предоставленному шаблону.
		if (encodingVersion is 0 or >= ProgramVersion)
#pragma warning restore CS8794 // Входные данные всегда соответствуют предоставленному шаблону.
		{
			var bytes2 = rfs.Length < 2048000000 ? default! : GC.AllocateUninitializedArray<byte>(2048000000);
			for (var i = 1; i < rfs.Length; i += 2048000000)
			{
				var length = (int)Min(rfs.Length - i, 2048000000);
				if (length < 2048000000)
					bytes2 = GC.AllocateUninitializedArray<byte>(length);
				rfs.ReadExactly(bytes2);
				wfs.Write(bytes2);
			}
			wfs.SetLength(wfs.Position);
			return;
		}
		if (continue_)
		{
			fragmentCount = 0;
			DecodeFibonacci(rfs, readByte);
			FragmentsMaximum = fragmentCount * ProgressBarStep;
		}
		using NList<byte> bytes = [], sizeBytes = RedStarLinq.NEmptyList<byte>(4);
		for (; fragmentCount > 0; fragmentCount--)
		{
			if (fragmentCount == 1)
			{
				bytes.Resize((int)Min(rfs.Length - rfs.Position, 2048000002));
				rfs.ReadExactly(bytes.AsSpan());
			}
			else
			{
				rfs.ReadExactly(sizeBytes.AsSpan(0, 3));
				var fragmentLength = Min(sizeBytes[0] * ValuesIn2Bytes + sizeBytes[1] * ValuesInByte + sizeBytes[2], 2048000002);
				if (fragmentLength > 16000010)
				{
					rfs.ReadExactly(sizeBytes.AsSpan());
					fragmentLength = Min(sizeBytes[0] * ValuesIn3Bytes + sizeBytes[1] * ValuesIn2Bytes + sizeBytes[2] * ValuesInByte + sizeBytes[3], 2048000002);
				}
				bytes.Resize(fragmentLength);
				rfs.ReadExactly(bytes.AsSpan());
			}
			var dec = new DecodingF();
			var s = new FragmentEncF(dec.Decode(bytes, encodingVersion)).Encode();
			dec.Dispose();
			if (fragmentCount != 1)
				wfs.Write([(byte)(s.Length >> (BitsPerByte << 1)), unchecked((byte)(s.Length >> BitsPerByte)), unchecked((byte)s.Length)]);
			wfs.Write(s.AsSpan());
			s.Dispose();
			Fragments += ProgressBarStep;
			GC.Collect();
		}
		if (wfs.Position > rfs.Length)
		{
			wfs.Seek(0, SeekOrigin.Begin);
			rfs.Seek(0, SeekOrigin.Begin);
			var bytes2 = rfs.Length < 2048000000 ? default! : GC.AllocateUninitializedArray<byte>(2048000000);
			for (var i = 0; i < rfs.Length; i += 2048000000)
			{
				var length = (int)Min(rfs.Length - i, 2048000000);
				if (length < 2048000000)
					bytes2 = GC.AllocateUninitializedArray<byte>(length);
				rfs.ReadExactly(bytes2);
				wfs.Write(bytes2);
			}
			wfs.SetLength(wfs.Position);
		}
		FragmentLength = PreservedFragmentLength;
	}

	private static void DecodeFibonacci(Stream rfs, byte readByte)
	{
		BitList bits;
		bool one = false, success = false;
		var sequencePos = 0;
		while (1 == 1)
		{
			if (sequencePos >= 2)
				readByte = (byte)rfs.ReadByte();
			bits = sequencePos < 2 ? new(2, (byte)(readByte >> 6)) : new(8, readByte);
			for (var i = 0; i < bits.Length; i++)
			{
				if (bits[i] && one || sequencePos == FibonacciSequence.Length)
				{
					success = true;
					break;
				}
				else
				{
					if (bits[i]) fragmentCount += FibonacciSequence[sequencePos];
					sequencePos++;
					one = bits[i];
				}
			}
			if (success)
				break;
		}
	}
}
