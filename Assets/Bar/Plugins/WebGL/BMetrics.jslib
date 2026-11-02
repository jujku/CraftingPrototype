mergeInto(LibraryManager.library, {
  BarBMetrics: function(ptr) {
    var row=JSON.parse(UTF8ToString(ptr));row.visibility=document.visibilityState;row.focus=document.hasFocus();row.browserTime=performance.now();
    window.bLatest=row;(window.bMetrics||(window.bMetrics=[])).push(row);if(window.bMetrics.length>2400)window.bMetrics.shift();
  }
});
