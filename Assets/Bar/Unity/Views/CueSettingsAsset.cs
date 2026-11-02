using UnityEngine;
namespace Bar.Unity
{
    /// <summary>Resources 入口只引用 Data/Json 中唯一一份线索配置，不复制用户美术资源。</summary>
    public sealed class CueSettingsAsset : ScriptableObject {public TextAsset Json;public TextAsset Optics;}
}
