﻿using DigitalRiseModel.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;

namespace DigitalRiseModel.Primitives
{
	internal class MeshBuilder
	{
		private bool _uses32BitIndices = false;

		public List<VertexPositionNormalTexture> Vertices = new List<VertexPositionNormalTexture>();
		private List<int> _indices { get; } = new List<int>();

		public int IndicesCount => _indices.Count;

		public void AddVertex(VertexPositionNormalTexture v) => Vertices.Add(v);

		public void AddIndex(int index)
		{
			if (index >= ushort.MaxValue)
			{
				_uses32BitIndices = true;
			}

			_indices.Add(index);
		}

		public void AddIndicesRange(IEnumerable<int> indices)
		{
			foreach (var idx in indices)
			{
				AddIndex(idx);
			}
		}

		public int GetIndex(int index) => _indices[index];
		public int[] CreateIndicesArray() => _indices.ToArray();

		public void ClearIndices()
		{
			_indices.Clear();
			_uses32BitIndices = false;
		}

		public NrmMeshPart CreateMeshPart(GraphicsDevice graphicsDevice, bool toLeftHanded)
		{
			if (toLeftHanded)
			{
				for (var i = 0; i < _indices.Count; i += 3)
				{
					var temp = _indices[i];
					_indices[i] = _indices[i + 2];
					_indices[i + 2] = temp;
				}

				for (var i = 0; i < Vertices.Count; ++i)
				{
					var v = Vertices[i];
					v.TextureCoordinate.X = 1.0f - v.TextureCoordinate.X;

					Vertices[i] = v;
				}
			}

			IndexBuffer indexBuffer;
			if (!_uses32BitIndices)
			{
				var indicesShort = new ushort[_indices.Count];
				for (var i = 0; i < indicesShort.Length; ++i)
				{
					indicesShort[i] = (ushort)_indices[i];
				}

				indexBuffer = indicesShort.CreateIndexBuffer(graphicsDevice);
			}
			else
			{
				indexBuffer = _indices.ToArray().CreateIndexBuffer(graphicsDevice);
			}

			var vertexBuffer = Vertices.ToArray().CreateVertexBuffer(graphicsDevice);


			return new NrmMeshPart(vertexBuffer, indexBuffer, BoundingBox.CreateFromPoints(from v in Vertices select v.Position));
		}
	}
}
