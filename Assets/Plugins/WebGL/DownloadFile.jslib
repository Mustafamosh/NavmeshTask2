mergeInto(LibraryManager.library, {

  DownloadFileFromUnity: function (filenamePtr, contentPtr) {
    var filename = UTF8ToString(filenamePtr);
    var content = UTF8ToString(contentPtr);

    var blob = new Blob([content], { type: 'application/x-ndjson' });
    var url = URL.createObjectURL(blob);

    var link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);

    URL.revokeObjectURL(url);
  }

});