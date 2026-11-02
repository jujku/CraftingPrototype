mergeInto(LibraryManager.library, {
  $barCopyState: { gl:null, ext:null, armed:false, pending:[], samples:[], programs:null, supported:false, error:null },
  BarArmCopyTimer__deps: ['$barCopyState'],
  BarArmCopyTimer: function() {
    var s=barCopyState;
    try {
      if(!s.gl) {
        var gl=GLctx;s.gl=gl;s.ext=gl.getExtension('EXT_disjoint_timer_query_webgl2');s.supported=!!s.ext;s.programs=new WeakMap();
        if(!s.ext)return;
        ['drawArrays','drawElements','drawArraysInstanced','drawElementsInstanced'].forEach(function(name) {
          var original=gl[name];if(!original)return;
          gl[name]=function() {
            if(!s.armed)return original.apply(gl,arguments);
            var program=gl.getParameter(gl.CURRENT_PROGRAM),blit=s.programs.get(program);
            if(blit===undefined) { blit=(gl.getAttachedShaders(program)||[]).some(function(shader){return (gl.getShaderSource(shader)||'').indexOf('_BlitTexture')>=0;});s.programs.set(program,blit); }
            if(!blit)return original.apply(gl,arguments);
            s.armed=false;
            // Measure only the fullscreen blit identified after the second-copy marker; no CPU wait or gl.finish.
            var query=gl.createQuery();gl.beginQuery(s.ext.TIME_ELAPSED_EXT,query);
            var result=original.apply(gl,arguments);gl.endQuery(s.ext.TIME_ELAPSED_EXT);s.pending.push(query);return result;
          };
        });
      }
      if(s.supported&&s.pending.length<4)s.armed=true;
    } catch(e) {s.error=String(e);s.armed=false;}
  },
  BarCopyMetrics__deps: ['$barCopyState'],
  BarCopyMetrics: function(cpuMs) {
    var s=barCopyState,gl=s.gl;
    try {
      if(gl&&s.ext) {
        var disjoint=gl.getParameter(s.ext.GPU_DISJOINT_EXT);
        s.pending=s.pending.filter(function(query){
          if(!gl.getQueryParameter(query,gl.QUERY_RESULT_AVAILABLE))return true;
          var elapsed=gl.getQueryParameter(query,gl.QUERY_RESULT);gl.deleteQuery(query);
          if(!disjoint)s.samples.push(elapsed/1000000);return false;
        });
      }
      var node=document.getElementById('copy-metrics');
      if(!node){node=document.createElement('pre');node.id='copy-metrics';document.body.appendChild(node);}
      node.textContent=JSON.stringify({cpuSubmissionMeanMs:cpuMs,gpuTimerSupported:s.supported,gpuBlitSamplesMs:s.samples,measurement:'first _BlitTexture draw following the Bar second-scene-copy marker; asynchronous EXT timer query; disjoint samples rejected',error:s.error});
    } catch(e) {s.error=String(e);}
  }
});
