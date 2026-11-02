using System;
using System.Runtime.Serialization;
using CraftingResearch.CoreV2;
namespace Bar.Logging
{
    /// <summary>版本化 JSONL 数据包。未适用字段省略；所有温度为摄氏度、时间为模型秒。</summary>
    [DataContract] public sealed class LogRecord
    {
        [DataMember(Order=0)] public string kind;
        public LogRecord Copy() => (LogRecord)MemberwiseClone();
        [DataMember(Order=1)] public long sequence;
        [DataMember(Order=2)] public double time;
        [DataMember(Order=3)] public string orderId="";
        [DataMember(EmitDefaultValue=false)] public int? attemptIndex;
        [DataMember(EmitDefaultValue=false)] public string orderDataSHA256,advance,servedGlass;
        [DataMember(EmitDefaultValue=false)] public OrderDimension[] dimensions;
        [DataMember(EmitDefaultValue=false)] public string[] feedbackIds;
        [DataMember(EmitDefaultValue=false)] public SessionHeader header;
        [DataMember(EmitDefaultValue=false)] public string carbonatedPourMethod;
        [DataMember(EmitDefaultValue=false)] public double? acceptedVolumeMl,co2InputGrams,co2LostOnEntryGrams,attemptDirectPourVolumeMl,attemptGuidedPourVolumeMl,attemptCo2LostDuringPour;
        [DataMember(EmitDefaultValue=false)] public int? selectedMenuIndex;
        [DataMember(EmitDefaultValue=false)] public string defaultActionId;
        [DataMember(EmitDefaultValue=false)] public string selectionInput;
        [DataMember(EmitDefaultValue=false)] public double? inputTimeSeconds,bottleRemainingMl,bottleCapacityMl;
        [DataMember(EmitDefaultValue=false)] public string[] availableActionIds;
        [DataMember(EmitDefaultValue=false)] public string actionId, activationId, hand, actorId, targetId, requestId, reason, amountUnit;
        [DataMember(EmitDefaultValue=false)] public string outcome;
        [DataMember(EmitDefaultValue=false)] public double? durationSeconds;
        [DataMember(EmitDefaultValue=false)] public InspectionObservation inspection;
        [DataMember(EmitDefaultValue=false)] public bool rejected, deferred, debugVisible, debugExposed;
        [DataMember(EmitDefaultValue=false)] public double? frameDeltaSeconds;
        [DataMember(EmitDefaultValue=false)] public double meanFrameSeconds,maxFrameSeconds;
        [DataMember(EmitDefaultValue=false)] public int renderedFrames;
        [DataMember(EmitDefaultValue=false)] public int? toolIceCubes;
        [DataMember(EmitDefaultValue=false)] public SoundObservation sound;
        [DataMember(EmitDefaultValue=false)] public double? tiltDegrees, heldSeconds, pressFraction, gestureStrength, intendedAmount, requestedAmount, actualAmount;
        [DataMember(EmitDefaultValue=false)] public VesselState[] before, after;
        [DataMember(EmitDefaultValue=false)] public CueFrame[] cues;
        [DataMember(EmitDefaultValue=false)] public long cueFrame;
        [DataMember(EmitDefaultValue=false)] public Operation operation;
        [DataMember(EmitDefaultValue=false)] public double? timeBefore;
        [DataMember(EmitDefaultValue=false)] public VesselState served;
        [DataMember(EmitDefaultValue=false)] public ProcessDescriptors process;
    }
    /// <summary>匿名会话头与完整回放数据。</summary>
    [DataContract] public sealed class SessionHeader
    {
        [DataMember] public string schemaVersion="bar-log-1.4",profile="v2",participantId,inputDevice="keyboard+mouse",experimentCondition="",processConvention="C2/freeform-1";
        [DataMember] public string uiVersion="speakeasy-direct-default-guided-v4";
        [DataMember] public int targetFrameRate;
        [DataMember] public double measuredFrameRate;
        [DataMember] public DataHash[] dataHashes;
        [DataMember(EmitDefaultValue=false)] public string iceTransferPolicy;
        [DataMember] public ModelData model;
        [DataMember(EmitDefaultValue=false)] public string qualityTier,qualitySettingsSHA256,postProcessingProfileSHA256,presentationSettingsSHA256,holdViewSettingsSHA256,qualitySettingsJson,postProcessingProfileJson;
        /// <summary>会话开始时的惯用手（Right/Left，V3）；之后的切换记为 handedness_changed 事件。</summary>
        [DataMember(EmitDefaultValue=false)] public string handedness;
    }
    /// <summary>检视开始时的只读物品状态；数值容器的完整状态同时引用该行before快照。</summary>
    [DataContract]public sealed class InspectionObservation
    {
        [DataMember] public string definitionId;
        [DataMember] public bool loaded,carbonated;
        [DataMember(EmitDefaultValue=false)] public double? bottleRemainingMl,bottleCapacityMl;
        [DataMember(EmitDefaultValue=false)] public bool? bottleOpen;
        [DataMember(EmitDefaultValue=false)] public string produceId;
        [DataMember(EmitDefaultValue=false)] public double? produceRemainingMl;
    }
    /// <summary>数据资源短名称和原始字节 SHA-256。</summary>
    [DataContract] public sealed class DataHash
    { [DataMember] public string name,sha256; }
    /// <summary>当前呈现帧的线索输出；可见强度已应用渲染和视锥开关，声音单独记录。</summary>
    [DataContract] public sealed class SoundObservation
    {
        [DataMember] public string category,bank,clip;
        [DataMember] public double gain,pitch;
        [DataMember(EmitDefaultValue=false)] public double scheduledDsp;
        [DataMember] public bool loop,started;
    }
    [DataContract] public sealed class CueFrame
    {
        [DataMember] public string vesselId;
        public CueFrame SampleCopy(){var c=(CueFrame)MemberwiseClone();c.frame=0;c.liquidRgba=liquidRgba==null?null:(double[])liquidRgba.Clone();return c;}
        [DataMember] public long frame;
        [DataMember] public bool onScreen,liquidVisible,iceVisible,seeInside;
        [DataMember] public double frost,condensation,foam,bubbleCount,bubbleSpeed,iceSound,dilution;
        [DataMember] public bool soundEmitted,soundPlaying;
        [DataMember] public double[] liquidRgba;
    }
    /// <summary>与 C2 对照的阶段、强度和剩冰统计。</summary>
    [DataContract] public sealed class ProcessDescriptors
    {
        [DataMember] public double actionSeconds,totalSeconds,meanIntensity,primarySeconds,postSeconds,remainingIce,restFraction;
    }
}
