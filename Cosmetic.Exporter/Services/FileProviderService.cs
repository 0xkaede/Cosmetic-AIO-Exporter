using Cosmetic.Exporter.Models;
using Cosmetic.Exporter.Utils;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Serilog.Core;
using System.Net;
using Logger = Cosmetic.Exporter.Utils.Logger;

namespace Cosmetic.Exporter.Services
{
    public interface IFileProviderService
    {
        public Task Init();
    }

    public class FileProviderService : IFileProviderService
    {
        public static DefaultFileProvider Provider { get; set; }

        public FileProviderService() 
        {
            if (!Directory.Exists(Constants.DataPath))
                Directory.CreateDirectory(Constants.DataPath);

            if (!Directory.Exists(Constants.ExportPath))
                Directory.CreateDirectory(Constants.ExportPath);
        }

        public async Task Init()
        {
            var aes = JsonConvert.DeserializeObject<FortniteAPIResponse<AES>>(await new HttpClient().GetStringAsync("https://fortnite-api.com/v2/aes"))!.Data;

            Provider = new DefaultFileProvider(FortniteUtils.PaksPath, SearchOption.AllDirectories, false, new VersionContainer(EGame.GAME_UE5_5));
            Provider.Initialize();

            var keys = new List<KeyValuePair<FGuid, FAesKey>>
            {
                new KeyValuePair<FGuid, FAesKey>(new FGuid(), new FAesKey(aes.MainKey))
            };

            keys.AddRange(aes.DynamicKeys.Select(x => new KeyValuePair<FGuid, FAesKey>(new Guid(x.PakGuid), new FAesKey(x.Key))));
            await Provider.SubmitKeysAsync(keys);

            Logger.Log($"File provider initalized with {Provider.Keys.Count} keys", LogLevel.Cue4);

            var oodlePath = Path.Combine(Constants.DataPath, OodleHelper.OODLE_DLL_NAME);
            if (File.Exists(OodleHelper.OODLE_DLL_NAME))
            {
                File.Move(OodleHelper.OODLE_DLL_NAME, oodlePath, true);
            }
            else if (!File.Exists(oodlePath))
            {
                await OodleHelper.DownloadOodleDllAsync(oodlePath);
            }

            OodleHelper.Initialize(oodlePath);

            var mappings = await Mappings();

            Provider.MappingsContainer = mappings;
        }

        private async Task<FileUsmapTypeMappingsProvider> Mappings()
        {
            var mappingsData = JsonConvert.DeserializeObject<List<MappingsResponse>>(await new HttpClient().GetStringAsync("https://fortnitecentral.genxgames.gg/api/v1/mappings"))[0];

            var path = Path.Combine(Constants.DataPath, mappingsData.FileName);
            if (!File.Exists(path))
            {
                Logger.Log($"Cant find latest mappings, Downloading {mappingsData.Url}", LogLevel.Cue4);

                var bytes = await new HttpClient().GetByteArrayAsync(mappingsData.Url);
                await File.WriteAllBytesAsync(path, bytes);
            }

            var latestUsmapInfo = new DirectoryInfo(Constants.DataPath).GetFiles("*_oo.usmap").FirstOrDefault(x => x.Name == mappingsData.FileName);

            Logger.Log(latestUsmapInfo != null ? $"Mappings Pulled from file: {latestUsmapInfo.Name}" : "Could not find mappings!", LogLevel.Cue4);

            return new FileUsmapTypeMappingsProvider(latestUsmapInfo.FullName);
        }
    }
}
