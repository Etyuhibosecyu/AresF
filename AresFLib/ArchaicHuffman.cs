namespace AresFLib;

internal record class ArchaicHuffman(int TN)
{
	public BitList Encode(NList<ShortIntervalList> input)
	{
		var bwtIndex = input[0].IndexOf(BWTApplied);
		if (CreateVar(input[0].IndexOf(HuffmanApplied), out var huffmanIndex) != -1 && !(bwtIndex != -1 && huffmanIndex == bwtIndex + 1))
			throw new EncoderFallbackException();
		Current[TN] = 0;
		CurrentMaximum[TN] = ProgressBarStep * 2;
		Status[TN] = 0;
		StatusMaximum[TN] = 3;
		var lz = CreateVar(input[0].IndexOf(LempelZivApplied), out var lzIndex) != -1 && (bwtIndex == -1 || lzIndex != bwtIndex + 1);
		var lzDummy = CreateVar(input[0].IndexOf(LempelZivDummyApplied), out var lzDummyIndex) != -1 && (bwtIndex == -1 || lzDummyIndex != bwtIndex + 1);
		var bwtLength = bwtIndex != -1 ? (int)input[0][bwtIndex + 1].Base : 0;
		var startPos = (lz || lzDummy ? (input[0].Length >= lzIndex + 2 && input[0][lzIndex + 1] == LempelZivSubdivided ? 3 : 2) : 1) + (input[0].Length >= 1 && input[0][0] == LengthsApplied ? (int)input[0][1].Base : 0) + bwtLength;
		Status[TN]++;
		var lzPos = bwtIndex != -1 ? 4 : 2;
		if (input.Length < startPos + lzPos + 1)
			throw new EncoderFallbackException();
		var originalBase = input[startPos + lzPos][0].Base;
		if (!input.GetRange(startPos + lzPos + 1).All((x, index) => bwtIndex != -1 && (index + lzPos + 1) % (BWTBlockSize + 2) is 0 or 1 || x[0].Base == originalBase))
			throw new EncoderFallbackException();
		var frequencyTable = input.GetRange(startPos).FrequencyTable(x => x[0].Lower).NSort(x => ~(uint)x.Count);
		var nodes = frequencyTable.ToList(x => new ArchaicHuffmanNode(x.Key, x.Count));
		var maxFrequency = nodes[0].Count;
		Current[TN] = 0;
		CurrentMaximum[TN] = ProgressBarStep * 2;
		Status[TN] = 0;
		StatusMaximum[TN] = nodes.Length - 1;
		Comparer<ArchaicHuffmanNode> comparer = new((x, y) => (~x.Count).CompareTo(~y.Count));
		var dic = frequencyTable.ToDictionary(x => x.Key, x => new BitList());
		while (nodes.Length > 1)
		{
			ArchaicHuffmanNode node = new(nodes[^1], nodes[^2]);
			nodes.RemoveEnd(^2);
			var pos = nodes.BinarySearch(node, comparer);
			if (pos < 0)
				pos = ~pos;
			nodes.Insert(pos, node);
			foreach (var x in node.Left!)
				dic[x].Add(false);
			foreach (var x in node.Right!)
				dic[x].Add(true);
			Status[TN]++;
		}
		dic.ForEach(x => x.Value.Reverse());
		BitList result = new((input.Length - startPos) * BitsPerByte);
		result.AddRange(EncodeFibonacci((uint)maxFrequency));
		result.AddRange(EncodeFibonacci((uint)frequencyTable.Length));
		Status[TN] = 0;
		StatusMaximum[TN] = frequencyTable.Length;
		Current[TN] += ProgressBarStep;
		for (var i = 0; i < frequencyTable.Length; i++, Status[TN]++)
		{
			result.AddRange(EncodeEqual(frequencyTable[i].Key, input[startPos + lzPos][0].Base));
			if (i != 0)
				result.AddRange(EncodeEqual((uint)frequencyTable[i].Count - 1, (uint)frequencyTable[i - 1].Count));
		}
		Status[TN] = 0;
		StatusMaximum[TN] = input.Length;
		Current[TN] += ProgressBarStep;
		for (var i = startPos; i < input.Length; i++, Status[TN]++)
		{
			result.AddRange(dic[input[i][0].Lower]);
			for (var j = 1; j < input[i].Length; j++)
				result.AddRange(EncodeEqual(input[i][j].Lower, input[i][j].Base));
		}
		return result;
	}
}
