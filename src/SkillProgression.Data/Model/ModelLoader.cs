using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using SkillProgression.Core.Model;

namespace SkillProgression.Data.Model
{
    /// <summary>Reads model.json / model.json.gz (built by training/build_model.py) into Core's ModelFile.</summary>
    public static class ModelLoader
    {
        public const int SupportedVersion = 1;

        public static ModelFile Load(string path)
        {
            using (var file = File.OpenRead(path))
            {
                var gz = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
                using (var stream = gz ? (Stream)new GZipStream(file, CompressionMode.Decompress) : file)
                    return Load(stream);
            }
        }

        public static ModelFile Load(Stream stream)
        {
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var json = new JsonTextReader(reader))
            {
                var model = JsonSerializer.CreateDefault().Deserialize<ModelFile>(json)
                            ?? throw new InvalidDataException("Empty model file.");
                if (model.Version > SupportedVersion)
                    throw new InvalidDataException($"Model version {model.Version} is newer than supported ({SupportedVersion}); update the plugin.");
                return model;
            }
        }
    }
}
