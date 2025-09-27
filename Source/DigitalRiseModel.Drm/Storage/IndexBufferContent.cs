﻿using DigitalRiseModel.Storage.Binary;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace DigitalRiseModel.Storage
{
	internal class IndexBufferContent
	{
		public int BufferId { get; set; }

		public IndexElementSize IndexType { get; set; }

		public int IndexCount { get; set; }

		[JsonIgnore]
		public byte[] Data { get; set; }

		public IndexBufferContent()
		{
		}


		public IndexBufferContent(List<uint> indices)
		{
			// Determine index type
			IndexType = IndexElementSize.SixteenBits;
			IndexCount = indices.Count;

			foreach (var idx in indices)
			{
				if (idx > ushort.MaxValue)
				{
					IndexType = IndexElementSize.ThirtyTwoBits;
					break;
				}
			}

			using (var ms = new MemoryStream())
			using (var writer = new BinaryWriter(ms))
			{
				if (IndexType == IndexElementSize.SixteenBits)
				{
					for (var i = 0; i < indices.Count; ++i)
					{
						writer.Write((ushort)indices[i]);
					}
				}
				else
				{
					for (var i = 0; i < indices.Count; ++i)
					{
						writer.Write(indices[i]);
					}
				}

				Data = ms.ToArray();
			}
		}

		internal void LoadBinaryData(ReadContext ctx)
		{
			Data = ctx.ReadByteArray(BufferId);

			if (Data.Length % IndexType.GetSize() != 0)
			{
				throw new Exception($"Inconsistent data size. Data.Length={Data.Length}, IndexSize={IndexType.GetSize()}");
			}

			IndexCount = Data.Length / IndexType.GetSize();
		}

		internal void SaveBinaryData(WriteContext ctx)
		{
			BufferId = ctx.WriteData(Data);
		}
	}
}
