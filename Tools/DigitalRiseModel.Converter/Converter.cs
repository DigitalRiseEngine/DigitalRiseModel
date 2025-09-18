﻿using Assimp;
using Assimp.Configs;
using DigitalRiseModel.Storage;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Quaternion = Microsoft.Xna.Framework.Quaternion;

namespace DigitalRiseModel.Converter
{
	internal class Converter
	{
		private struct Bool4
		{
			public bool X;
			public bool Y;
			public bool Z;
			public bool W;
		}

		private ModelContent _model;
		private readonly List<uint> _indices = new List<uint>();
		private readonly List<SubmeshContent> _submeshes = new List<SubmeshContent>();
		private readonly List<BoneContent> _bones = new List<BoneContent>();
		private readonly Dictionary<string, byte> _bonesIndices = new Dictionary<string, byte>();
		private readonly List<DrMaterial> _materials = new List<DrMaterial>();

		static void Log(string message) => Console.WriteLine(message);

		private void BuildBonesIndices(Node root, ref byte index)
		{
			_bonesIndices[root.Name] = index;
			++index;

			if (root.HasChildren)
			{
				for (var i = 0; i < root.ChildCount; ++i)
				{
					BuildBonesIndices(root.Children[i], ref index);
				}
			}
		}

		private byte GetBoneIndex(string name)
		{
			byte result;
			if (!_bonesIndices.TryGetValue(name, out result))
			{
				throw new Exception($"Unable to find bone {name}");
			}

			return result;
		}

		private int FindVertexBuffer(List<VertexElementContent> vertexElements)
		{
			for (var i = 0; i < _model.VertexBuffers.Count; ++i)
			{
				var vertexBuffer = _model.VertexBuffers[i];
				if (vertexBuffer.Elements.Count != vertexElements.Count ||
					vertexBuffer.VertexStride != vertexElements.CalculateStride())
				{
					continue;
				}

				var match = true;
				for (var j = 0; j < vertexElements.Count; ++j)
				{
					if (!VertexElementContent.AreEqual(vertexBuffer.Elements[j], vertexElements[j]))
					{
						match = false;
						break;
					}
				}

				if (match)
				{
					return i;
				}
			}

			// Create new vertex buffer
			var newVertexBuffer = new VertexBufferContent();
			for (var i = 0; i < vertexElements.Count; ++i)
			{
				newVertexBuffer.Elements.Add(vertexElements[i]);
			}

			_model.VertexBuffers.Add(newVertexBuffer);

			return _model.VertexBuffers.Count - 1;
		}

		private List<VertexElementContent> BuildVertexElement(Mesh mesh)
		{
			if (!mesh.HasVertices)
			{
				throw new Exception($"Mesh {mesh.Name} has no vertices. Such meshes aren't supported.");
			}

			var vertexElements = new List<VertexElementContent>();
			vertexElements.Add(new VertexElementContent(VertexElementUsage.Position, VertexElementFormat.Vector3));

			if (mesh.HasNormals)
			{
				vertexElements.Add(new VertexElementContent(VertexElementUsage.Normal, VertexElementFormat.Vector3));
			}

			for (var i = 0; i < mesh.VertexColorChannelCount; ++i)
			{
				vertexElements.Add(new VertexElementContent(VertexElementUsage.Color, VertexElementFormat.Color, i));
			}

			for (var i = 0; i < mesh.TextureCoordinateChannelCount; ++i)
			{
				switch (mesh.UVComponentCount[i])
				{
					case 2:
						vertexElements.Add(new VertexElementContent(VertexElementUsage.TextureCoordinate, VertexElementFormat.Vector2, i));
						break;

					default:
						throw new Exception($"UWComponentCount {mesh.UVComponentCount[i]} isn't supported.");
				}
			}

			if (mesh.HasTangentBasis)
			{
				vertexElements.Add(new VertexElementContent(VertexElementUsage.Tangent, VertexElementFormat.Vector3));
				vertexElements.Add(new VertexElementContent(VertexElementUsage.Binormal, VertexElementFormat.Vector3));
			}

			if (mesh.HasBones)
			{
				vertexElements.Add(new VertexElementContent(VertexElementUsage.BlendIndices, VertexElementFormat.Byte4));
				vertexElements.Add(new VertexElementContent(VertexElementUsage.BlendWeight, VertexElementFormat.Vector4));
			}


			return vertexElements;
		}

		private byte[] BuildVertexBufferData(Mesh mesh)
		{
			var vertexCount = mesh.Vertices.Count;

			Byte4[] boneIndices = null;
			Vector4[] boneWeights = null;

			if (mesh.HasBones)
			{
				// Fill bone arrays
				var boneSets = new Bool4[vertexCount];
				boneIndices = new Byte4[vertexCount];
				boneWeights = new Vector4[vertexCount];

				for (var j = 0; j < mesh.Bones.Count; ++j)
				{
					var bone = mesh.Bones[j];
					var boneIndex = (byte)j;

					for (var k = 0; k < bone.VertexWeightCount; ++k)
					{
						var vertexWeight = bone.VertexWeights[k];
						var vertexId = vertexWeight.VertexID;
						var weight = vertexWeight.Weight;
						if (weight.IsZero())
						{
							continue;
						}

						var bs = boneSets[vertexId];
						var bi = boneIndices[vertexId].ToVector4();
						var w = boneWeights[vertexId];

						var bx = (byte)bi.X;
						var by = (byte)bi.Y;
						var bz = (byte)bi.Z;
						var bw = (byte)bi.W;

						if (!bs.X || weight > w.X)
						{
							bs.X = true;
							bx = boneIndex;
							w.X = weight;
						}
						else if (!bs.Y || weight > w.Y)
						{
							bs.Y = true;
							by = boneIndex;
							w.Y = weight;
						}
						else if (!bs.Z || weight > w.Z)
						{
							bs.Z = true;
							bz = boneIndex;
							w.Z = weight;
						}
						else if (!bs.W || weight > w.W)
						{
							bs.W = true;
							bw = boneIndex;
							w.W = weight;
						}
						else
						{
							throw new Exception($"Vertex {vertexId} has more than 4 bones");
						}

						boneSets[vertexId] = bs;
						boneIndices[vertexId] = new Byte4(bx, by, bz, bw);

						// Normalize weight
						var totalWeight = w.X + w.Y + w.Z + w.W;
						if (!totalWeight.IsZero())
						{
							w.X /= totalWeight;
							w.Y /= totalWeight;
							w.Z /= totalWeight;
							w.W /= totalWeight;
						}

						boneWeights[vertexId] = w;
					}
				}
			}

			using (var ms = new MemoryStream())
			using (var writer = new BinaryWriter(ms))
			{
				for (var i = 0; i < vertexCount; ++i)
				{
					writer.Write(mesh.Vertices[i].ToXna());

					if (mesh.HasNormals)
					{
						writer.Write(mesh.Normals[i].ToXna());
					}

					for (var j = 0; j < mesh.VertexColorChannelCount; ++j)
					{
						writer.Write(mesh.VertexColorChannels[j][i].ToXna());
					}

					for (var j = 0; j < mesh.TextureCoordinateChannelCount; ++j)
					{
						switch (mesh.UVComponentCount[j])
						{
							case 2:
								writer.Write(mesh.TextureCoordinateChannels[j][i].ToXnaVector2());
								break;

							default:
								throw new Exception($"UWComponentCount {mesh.UVComponentCount[j]} isn't supported.");
						}
					}

					if (mesh.HasTangentBasis)
					{
						writer.Write(mesh.Tangents[i].ToXna());
						writer.Write(mesh.BiTangents[i].ToXna());
					}

					if (mesh.HasBones)
					{
						writer.Write(boneIndices[i].PackedValue);
						writer.Write(boneWeights[i]);
					}
				}

				return ms.ToArray();
			}
		}

		private void ProcessMeshes(Scene scene)
		{
			foreach (var mesh in scene.Meshes)
			{
				if (mesh.PrimitiveType != Assimp.PrimitiveType.Triangle)
				{
					throw new Exception("Only triangle primitive type is supported");
				}

				var vertexElements = BuildVertexElement(mesh);

				var vertexBufferIndex = FindVertexBuffer(vertexElements);
				var vertexBuffer = _model.VertexBuffers[vertexBufferIndex];

				var startVertex = vertexBuffer.MemoryVertexCount;

				var data = BuildVertexBufferData(mesh);
				vertexBuffer.Write(data);

				var startIndex = _indices.Count;
				var indices = mesh.GetUnsignedIndices();

				_indices.AddRange(indices);

				var submesh = new SubmeshContent
				{
					VertexBufferIndex = vertexBufferIndex,
					StartVertex = startVertex,
					VertexCount = mesh.Vertices.Count,
					StartIndex = startIndex,
					PrimitiveCount = indices.Length / 3,
					MaterialIndex = mesh.MaterialIndex,
					BoundingBox = Microsoft.Xna.Framework.BoundingBox.CreateFromPoints((from v in mesh.Vertices select v.ToXna()).ToArray())
				};

				_submeshes.Add(submesh);
			}

			_model.IndexBuffer = new IndexBufferContent(_indices);
		}

		BoneContent Convert(Node node)
		{
			var transform = node.Transform.ToXna();
			Vector3 scale, translation;
			Quaternion rotation;

			transform.Decompose(out scale, out rotation, out translation);

			var result = new BoneContent
			{
				Name = node.Name,
				Scale = scale,
				Rotation = rotation,
				Translation = translation
			};

			_bones.Add(result);

			if (node.HasMeshes)
			{
				result.Mesh = new MeshContent();

				foreach (var meshIndex in node.MeshIndices)
				{
					result.Mesh.Submeshes.Add(_submeshes[meshIndex]);
				}
			}

			if (node.Children != null)
			{
				foreach (var child in node.Children)
				{
					result.Children.Add(Convert(child));
				}
			}

			return result;
		}

		private static string UpdateMaterialPath(string texturePath, string modelFolder)
		{
			if (!string.IsNullOrEmpty(texturePath) && !Path.IsPathRooted(texturePath))
			{
				texturePath = Path.Combine(modelFolder, texturePath);
			}

			return texturePath;
		}

		private void ProcessMaterials(Scene scene, string inputPath)
		{
			var modelFolder = Path.GetDirectoryName(inputPath);

			for (var i = 0; i < scene.MaterialCount; ++i)
			{
				var sourceMaterial = scene.Materials[i];

				var material = new DrMaterial();

				/*				if (material.HasBlendMode)
								{
									materialContent.Properties["BlendMode"] = material.BlendMode;
								}*/

/*				if (material.HasBumpScaling)
				{
					materialContent.BumpScaling = material.BumpScaling;
				}

				if (material.HasColorAmbient)
				{
					materialContent.AmbientColor = material.ColorAmbient.ToXna();
				}*/

				if (sourceMaterial.HasColorDiffuse)
				{
					material.DiffuseColor = sourceMaterial.ColorDiffuse.ToXna();
				}

/*				if (material.HasColorEmissive)
				{
					materialContent.EmissiveColor = material.ColorEmissive.ToXna();
				}

				if (material.HasColorReflective)
				{
					materialContent.ReflectiveColor = material.ColorReflective.ToXna();
				}*/

				if (sourceMaterial.HasColorSpecular)
				{
					material.SpecularColor = sourceMaterial.ColorSpecular.ToXna();
				}

/*				if (material.HasColorTransparent)
				{
					materialContent.TransparentColor = material.ColorTransparent.ToXna();
				}

				if (material.HasOpacity)
				{
					materialContent.Opacity = material.Opacity;
				}

				if (material.HasReflectivity)
				{
					materialContent.Reflectivity = material.Reflectivity;
				}

								if (material.HasShadingMode)
								{
									materialContent.Properties["ShadingMode"] = material.ShadingMode;
								}*/

				if (sourceMaterial.HasShininess)
				{
					material.SpecularPower = sourceMaterial.Shininess;
				}

/*				if (material.HasShininessStrength)
				{
					materialContent.ShininessStrength = material.ShininessStrength;
				}

				if (material.HasTextureAmbient)
				{
					materialContent.AmbientTexture = material.TextureAmbient.ToTextureSlotContent();
				}

				if (material.HasTextureAmbientOcclusion)
				{
					materialContent.AmbientOcclusionTexture = material.TextureAmbientOcclusion.ToTextureSlotContent();
				}*/

				if (sourceMaterial.HasTextureDiffuse)
				{
					material.DiffuseTexture = UpdateMaterialPath(sourceMaterial.TextureDiffuse.FilePath, modelFolder);
				}

/*				if (material.HasTextureEmissive)
				{
					materialContent.EmissiveTexture = material.TextureEmissive.ToTextureSlotContent();
				}

				if (material.HasTextureHeight)
				{
					materialContent.HeightTexture = material.TextureHeight.ToTextureSlotContent();
				}

				if (material.HasTextureLightMap)
				{
					materialContent.LightMapTexture = material.TextureLightMap.ToTextureSlotContent();
				}*/

				if (sourceMaterial.HasTextureNormal)
				{
					material.NormalTexture = UpdateMaterialPath(sourceMaterial.TextureNormal.FilePath, modelFolder);
				}

/*				if (material.HasTextureOpacity)
				{
					materialContent.OpacityTexture = material.TextureOpacity.ToTextureSlotContent();
				}

				if (material.HasTextureReflection)
				{
					materialContent.ReflectionTexture = material.TextureReflection.ToTextureSlotContent();
				}*/

				if (sourceMaterial.HasTextureSpecular)
				{
					material.SpecularTexture = UpdateMaterialPath(sourceMaterial.TextureSpecular.FilePath, modelFolder);
				}

/*				if (material.HasTransparencyFactor)
				{
					materialContent.TransparencyFactor = material.TransparencyFactor;
				}

				if (material.HasTwoSided)
				{
					materialContent.IsTwoSided = material.IsTwoSided;
				}

				if (material.HasWireFrame)
				{
					materialContent.IsWireFrame = material.IsWireFrameEnabled;
				}*/

				_materials.Add(material);
			}

			if (_materials.Count > 0)
			{
				_model.Materials = _materials.ToArray();
			}
		}

		private void ProcessSkins(Scene scene)
		{
			for (var i = 0; i < scene.Meshes.Count; ++i)
			{
				var mesh = scene.Meshes[i];
				if (!mesh.HasBones)
				{
					continue;
				}

				var skinContent = new SkinContent();
				for (var j = 0; j < mesh.Bones.Count; ++j)
				{
					var bone = mesh.Bones[j];
					var boneIndex = GetBoneIndex(bone.Name);

					var skinJointContent = new SkinJointContent
					{
						BoneIndex = boneIndex,
						InverseBindTransform = bone.OffsetMatrix.ToXna()
					};

					skinContent.Data.Add(skinJointContent);
				}

				_submeshes[i].Skin = skinContent;
			}
		}

		private void ProcessAnimations(Scene scene)
		{
			foreach (var animation in scene.Animations)
			{
				if (animation.HasMeshAnimations)
				{
					throw new Exception($"Mesh animations aren't supported. Animaton name='{animation.Name}'.");
				}

				var animationClip = new AnimationClipContent
				{
					Name = animation.Name
				};
				foreach (var sourceChannel in animation.NodeAnimationChannels)
				{
					var boneIndex = GetBoneIndex(sourceChannel.NodeName);
					var channel = new AnimationChannelContent
					{
						BoneIndex = boneIndex
					};

					// Translations
					if (sourceChannel.HasPositionKeys)
					{
						for (var i = 0; i < sourceChannel.PositionKeyCount; ++i)
						{
							var pos = sourceChannel.PositionKeys[i];
							channel.Translations.Data.Add(new VectorKeyframeContent(pos.Time, pos.Value.ToXna()));
						}
					}

					// Scales
					if (sourceChannel.HasScalingKeys)
					{
						for (var i = 0; i < sourceChannel.ScalingKeyCount; ++i)
						{
							var scale = sourceChannel.ScalingKeys[i];
							channel.Scales.Data.Add(new VectorKeyframeContent(scale.Time, scale.Value.ToXna()));
						}
					}

					// Rotations
					if (sourceChannel.HasRotationKeys)
					{
						for (var i = 0; i < sourceChannel.RotationKeyCount; ++i)
						{
							var rotation = sourceChannel.RotationKeys[i];
							channel.Rotations.Data.Add(new QuaternionKeyframeContent(rotation.Time, rotation.Value.ToXna()));
						}
					}

					animationClip.Channels.Add(channel);
				}

				_model.Animations[animationClip.Name] = animationClip;
			}
		}

		public void Convert(Options options)
		{
			var time = DateTime.Now;

			_model = new ModelContent();
			_bones.Clear();
			_bonesIndices.Clear();
			_indices.Clear();
			_submeshes.Clear();
			_materials.Clear();

			using (AssimpContext importer = new AssimpContext())
			{
				importer.SetConfig(new VertexBoneWeightLimitConfig(4));

				/*				var steps = PostProcessSteps.FindDegenerates |
									PostProcessSteps.FindInvalidData |
									PostProcessSteps.FlipUVs |              // Required for Direct3D
									PostProcessSteps.FlipWindingOrder |     // Required for Direct3D
									PostProcessSteps.JoinIdenticalVertices |
									PostProcessSteps.ImproveCacheLocality |
									PostProcessSteps.OptimizeMeshes |
									PostProcessSteps.Triangulate;*/

				var steps = PostProcessSteps.FindDegenerates |
					PostProcessSteps.FindInvalidData |
					PostProcessSteps.FlipUVs |              // Required for Direct3D
					PostProcessSteps.ImproveCacheLocality |
					PostProcessSteps.OptimizeMeshes |
					PostProcessSteps.Triangulate;

				if (!options.FlipWindingOrder)
				{
					steps |= PostProcessSteps.FlipWindingOrder;
				}

				if (options.GenerateTangentsAndBitangents)
				{
					steps |= PostProcessSteps.CalculateTangentSpace;
				}

				var scene = importer.ImportFile(options.InputFile, steps);

				byte index = 0;
				BuildBonesIndices(scene.RootNode, ref index);
				ProcessMeshes(scene);

				_model.RootBone = Convert(scene.RootNode);

				ProcessMaterials(scene, options.InputFile);
				ProcessSkins(scene);
				ProcessAnimations(scene);
			}

			var outputFolder = Path.GetDirectoryName(options.OutputFile);
			if (!string.IsNullOrEmpty(outputFolder) && !Directory.Exists(outputFolder))
			{
				Log($"Creating folder '{outputFolder}' doesn't exist.");
				Directory.CreateDirectory(outputFolder);
			}

			string outputFile;
			if (options.OutputFile.EndsWith(".drm", StringComparison.OrdinalIgnoreCase))
			{
				outputFile = Path.ChangeExtension(options.OutputFile, "drm");
				_model.SaveBinaryToFile(outputFile, options.OverwriteModelFile, Log);
			}
			else
			{
				outputFile = Path.ChangeExtension(options.OutputFile, "jdrm");
				var binaryFile = Path.ChangeExtension(options.OutputFile, "bin");
				_model.SaveJsonToFile(outputFile, binaryFile, options.OverwriteModelFile, Log);
			}

			var passed = DateTime.Now - time;
			Log($"{passed.TotalMilliseconds} ms");
		}
	}
}
