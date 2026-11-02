mergeInto(LibraryManager.library, {
  VisualMetrics: function(fps, iceMs, label) {
    var node=document.getElementById('visual-metrics');
    if(!node){node=document.createElement('div');node.id='visual-metrics';node.style='position:fixed;top:0;left:0;background:#14202c;color:white;padding:8px;z-index:9999;font:14px monospace';document.body.appendChild(node);}
    node.textContent=UTF8ToString(label)+' | '+fps.toFixed(1)+' fps | ice '+iceMs.toFixed(3)+' ms';
    node.setAttribute('data-fps',fps.toFixed(3));node.setAttribute('data-ice-ms',iceMs.toFixed(4));
  }
});
