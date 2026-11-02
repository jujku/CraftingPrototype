mergeInto(LibraryManager.library, {
  BarQMetrics: function(value) { window.qLatest = JSON.parse(UTF8ToString(value)); },
  BarQAudioContextTime__deps: ['$WEBAudio'],
  BarQAudioContextTime: function() { return WEBAudio.audioContext ? WEBAudio.audioContext.currentTime : 0; }
});
