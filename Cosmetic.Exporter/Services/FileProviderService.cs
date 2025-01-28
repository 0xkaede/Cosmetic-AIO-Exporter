using Cosmetic.Exporter.Enums;
using Cosmetic.Exporter.Models;
using Cosmetic.Exporter.Utils;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.GameTypes.FN.Assets.Exports.Sound;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Rig;
using CUE4Parse.UE4.Assets.Exports.Sound.Node;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Sounds;
using CUE4Parse_Conversion.Textures;
using Newtonsoft.Json;
using Serilog.Core;
using SkiaSharp;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
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

            return new FileUsmapTypeMappingsProvider(latestUsmapInfo!.FullName);
        }

        private ExportData _exportData = new ExportData();

        private string _currentId { get; set; }

        private string AnimationsPath() => $"{Constants.ExportPath}\\{_currentId}\\Animations";
        private string JsonPath() => $"{AnimationsPath()}\\Json";
        private string IconsPath() => $"{Constants.ExportPath}\\{_currentId}\\Icons";
        private string MiscPath() => $"{Constants.ExportPath}\\{_currentId}\\Misc";

        private void MoveAnimations(string filePath) => File.Move(filePath, $"{AnimationsPath()}\\{Path.GetFileName(filePath)}");

        private async Task JsonEmoteDataSave()
            => await File.WriteAllTextAsync($"{Constants.ExportPath}\\{_currentId}\\Misc\\Data.json",
                JsonConvert.SerializeObject(_exportData, Formatting.Indented));

        private async Task JsonDataSave(string data)
            => await File.WriteAllTextAsync($"{Constants.ExportPath}\\{_currentId}\\Misc\\Montage.json",
                data);

        public async Task ExportCosmetic(string Id, ItemType type)
        {
            _currentId = Id;
            UObject cosmeticObject = null;

            cosmeticObject = await Provider.LoadObjectAsync($"FortniteGame/Plugins/GameFeatures/BRCosmetics/Content/Athena/Items/Cosmetics/Dances/{Id}");

            if (cosmeticObject is null)
            {
                Logger.Log("Failed Getting object", LogLevel.Error);
                return;
            }

            if (!cosmeticObject.TryGetValue(out FText displayName, "ItemName"))
                Logger.Log("Error Getting DisplayName", LogLevel.Error);

            if (!cosmeticObject.TryGetValue(out FText description, "ItemDescription"))
                Logger.Log("Error Getting Description", LogLevel.Error);

            _exportData.Name = displayName.Text ?? "Invalid";
            _exportData.Description = description.Text ?? "Invalid";

            switch (type)
            {
                case ItemType.Emote:
                    {
                        _exportData.Emote = new EmoteData();
                        await ExportEmote(cosmeticObject);

                        break;
                    }
            }
        }

        #region EMOTE
        
        private async Task ExportEmote(UObject eidObject)
        {
            if(!eidObject.TryGetValue(out UObject CMM_Montage, "Animation"))
            {
                Logger.Log("Error Getting \"Animation\"", LogLevel.Error);
                return;
            }

            if (!eidObject.TryGetValue(out UObject CMF_Montage, "AnimationFemaleOverride"))
            {
                Logger.Log("Error Getting \"AnimationFemaleOverride\"", LogLevel.Error);
                return;
            }

            if (eidObject.TryGetValue(out bool bMovingEmote, "bMovingEmote"))
            {
                _exportData.Emote.IsMovingEmote = bMovingEmote;

                if (eidObject.TryGetValue(out bool bMoveForwardOnly, "bMoveForwardOnly"))
                    _exportData.Emote.IsMoveForwardOnly = bMoveForwardOnly;

                if (eidObject.TryGetValue(out float walkForwardSpeed, "WalkForwardSpeed"))
                    _exportData.Emote.WalkForwardSpeed = walkForwardSpeed;
            }

            Logger.Log($"Exporting Male Animations {CMM_Montage.Name}", LogLevel.Cue4);
            if (CMM_Montage.TryGetValue(out FSlotAnimationTrack[] slotAnimTracks, "SlotAnimTracks"))
            {
                await ExportAnimations(CMM_Montage, EGender.Male, slotAnimTracks);
            }

            Logger.Log($"Exporting Female Animations {CMF_Montage.Name}", LogLevel.Cue4);
            if (CMF_Montage.TryGetValue(out FSlotAnimationTrack[] slotAnimTracksF, "SlotAnimTracks"))
            {
                await ExportAnimations(CMF_Montage, EGender.Female, slotAnimTracksF);
            }

            Directory.CreateDirectory(MiscPath());

            await ExportIcons(eidObject);

            Logger.Log("Exporting Audio", LogLevel.Cue4);
            await ExportAudio(CMM_Montage);

            if(CMM_Montage != null)
            {
                if(CMM_Montage.TryGetValue(out FStructFallback blendIn, "BlendIn"))
                {
                    if (blendIn.TryGetValue(out float fBlendIn, "BlendTime"))
                        _exportData.Emote.Blend.Add("BlendIn", fBlendIn);
                }

                if (CMM_Montage.TryGetValue(out FStructFallback blendOut, "BlendOut"))
                {
                    if (blendIn.TryGetValue(out float fBlendOut, "BlendTime"))
                        _exportData.Emote.Blend.Add("BlendOut", fBlendOut);
                }
            }

            await JsonDataSave(JsonConvert.SerializeObject(CMM_Montage, Formatting.Indented));
            //await GetLastData(); redo
            await JsonEmoteDataSave();
        }

        private async Task ExportAnimations(UObject uObject, EGender gender = EGender.Male, FSlotAnimationTrack[] slotAnimTracks = null)
        {
            bool isAdictive = false;

            if (gender is EGender.Female)
                isAdictive = slotAnimTracks.FirstOrDefault(x => x.SlotName.PlainText == "AdditiveCorrective") is null ? false : true;

            _exportData.Emote.IsAddictive = isAdictive;

            var currentSlotName = isAdictive ? "FullBody" : "AdditiveCorrective";

            var fullBodyAnimTrack = slotAnimTracks.FirstOrDefault(x => x.SlotName.PlainText == "FullBody");

            if (fullBodyAnimTrack is null)
            {
                Logger.Log($"Cant find FullBody", LogLevel.Error);
                return;
            }

            var animReference = await fullBodyAnimTrack.AnimTrack.AnimSegments[0].AnimReference.LoadAsync();
            if(animReference != null)
            {
                if (isAdictive)
                {
                    var additiveCorrectiveAnimTrack = slotAnimTracks.FirstOrDefault(x => x.SlotName.PlainText == "AdditiveCorrective");

                    if (additiveCorrectiveAnimTrack != null)
                    {
                        var additiveCorrectiveanimReference = await additiveCorrectiveAnimTrack.AnimTrack.AnimSegments[0].AnimReference.LoadAsync();
                        if (additiveCorrectiveanimReference != null)
                        {
                            var refUAnimSequence = await Provider.LoadObjectAsync<UAnimSequence>(animReference.GetPathName());
                            var addUAnimSequence = await Provider.LoadObjectAsync<UAnimSequence>(additiveCorrectiveanimReference.GetPathName());
                            addUAnimSequence.RefPoseSeq = new ResolvedLoadedObject(refUAnimSequence);
                            await ExportAnimation(addUAnimSequence);
                        }
                    }
                    else
                        Logger.Log($"additiveCorrectiveAnimTrack is null, Stoping action!", LogLevel.Error);
                }
                else
                {
                    var refUAnimSequence = await Provider.LoadObjectAsync<UAnimSequence>(animReference.GetPathName());
                    if(refUAnimSequence != null)
                        await ExportAnimation(refUAnimSequence);
                }

                if (_exportData.Emote.IsMovingEmote)
                {
                    var bodyMotion = slotAnimTracks.FirstOrDefault(x => x.SlotName.PlainText is "FullBodyInMotion");
                    if (bodyMotion != null)
                    {
                        var animReferenceMot = await bodyMotion.AnimTrack.AnimSegments[0].AnimReference.LoadAsync();
                        var animSequence = await Provider.LoadObjectAsync<UAnimSequence>(animReferenceMot!.GetPathName());
                        await ExportAnimation(animSequence);
                    }
                }
            }
            else
                Logger.Log($"animReference is null, Stoping action!", LogLevel.Error);
        }

        private async Task ExportAnimation(UAnimSequence animSequence)
        {
            var exporterOptions = new ExporterOptions()
            {
                AnimFormat = EAnimFormat.ActorX
            };

            var animExporter = new AnimExporter(animSequence, exporterOptions);
            animExporter.TryWriteToDir(new DirectoryInfo(Constants.ExportPath), out var label, out var fileName);

            Logger.Log($"Exported {Path.GetFileNameWithoutExtension(fileName)}", LogLevel.Cue4);

            if (!Directory.Exists(AnimationsPath()))
                Directory.CreateDirectory(AnimationsPath());

            MoveAnimations(fileName);

            Directory.CreateDirectory($"{JsonPath()}");
            await File.WriteAllTextAsync(JsonPath() + $"\\{Path.GetFileNameWithoutExtension(fileName)}.json",
                JsonConvert.SerializeObject(new List<UAnimSequence>() { animSequence }, Formatting.Indented));
        }

        private async Task ExportIcons(UObject uObject)
        {
            Logger.Log("Exporting Icons", LogLevel.Cue4);

            Directory.CreateDirectory(IconsPath());

            if (uObject.TryGetValue(out FInstancedStruct[] dataList, "DataList"))
            {
                foreach (var data in dataList)
                {
                    var constStruct = data.NonConstStruct;
                    if(constStruct is null)
                        continue;

                    if (constStruct.TryGetValue(out UTexture2D iconSmall, "Icon"))
                    {
                        await File.WriteAllBytesAsync(IconsPath() + $"\\{iconSmall.Name}.png", iconSmall.Decode()!.Encode(SKEncodedImageFormat.Png, 256).ToArray());
                        continue;
                    }

                    if (constStruct.TryGetValue(out UTexture2D largeSmall, "LargeIcon"))
                    {
                        await File.WriteAllBytesAsync(IconsPath() + $"\\{largeSmall.Name}.png", largeSmall.Decode()!.Encode(SKEncodedImageFormat.Png, 512).ToArray());
                        continue;
                    }
                }
            }

            Logger.Log("Exported Icons", LogLevel.Cue4);
        }

        private async Task ExportAudio(UObject uObject)
        {
            if (uObject.TryGetValue(out FAnimNotifyEvent[] events, "Notifies"))
            {
                foreach (var sound in events)
                    if (sound.NotifyName.PlainText.Contains("FortEmoteSound") || sound.NotifyName.PlainText.Contains("Fort Anim Notify State Emote Sound"))
                    {
                        var musicClass = await sound.NotifyStateClass.LoadAsync();

                        if (musicClass.TryGetValue(out UEmoteMusic emoteSound1P, "EmoteSound1P"))
                        {
                            var soundRandom = await TryGetSoundRandom(emoteSound1P);

                            var soundNode = await soundRandom.ChildNodes.FirstOrDefault().TryLoadAsync<USoundNodeWavePlayer>();

                            var musicBoom = await soundNode.SoundWave.LoadAsync();

                            musicBoom.Decode(false, out var audioFormat, out var data);

                            Directory.CreateDirectory(MiscPath());

                            await File.WriteAllBytesAsync($"{MiscPath()}\\{musicBoom.Name}.blinka", data);

                            await FixSound($"{MiscPath()}\\{musicBoom.Name}.blinka");

                            Logger.Log($"Exported {musicBoom.Name}", LogLevel.Cue4);
                        }
                        else
                            Logger.Log("Error Getting UEmoteMusic", LogLevel.Error);
                    }
            }
            else
                Logger.Log("Error Getting FAnimNotifyEvent", LogLevel.Error);
        }

        private static async Task FixSound(string path)
        {
            if (!File.Exists(Constants.BlinkaExe))
            {
                var bytes = await new HttpClient().GetByteArrayAsync($"https://cdn.0xkaede.xyz/binkadec.exe");

                if (bytes is null)
                    Logger.Log("Decode exe is null");
                else
                    await File.WriteAllBytesAsync(Constants.BlinkaExe, bytes);
            }

            var binkadecProcess = Process.Start(new ProcessStartInfo
            {
                FileName = Constants.BlinkaExe,
                Arguments = $"-i \"{path}\" -o \"{path.Replace("blinka", "wav")}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            binkadecProcess?.WaitForExit(5000);
        }

        private static async Task<USoundNodeRandom> TryGetSoundRandom(UEmoteMusic uEmoteMusic)
        {
            try
            {
                var data = await uEmoteMusic.FirstNode.LoadAsync<UFortSoundNodeLicensedContentSwitcher>();
                return await data.ChildNodes[1].LoadAsync<USoundNodeRandom>();
            }
            catch
            {
                return await uEmoteMusic!.FirstNode.LoadAsync<USoundNodeRandom>();
            }
        }

        #endregion
    }
}
