window.excelEditor = window.excelEditor || {};

window.excelEditor.downloadFile = (fileName, base64Data) => {
    if (!fileName || !base64Data) {
        return;
    }

    const link = document.createElement('a');
    link.style.display = 'none';
    link.download = fileName;
    link.href = `data:application/vnd.openxmlformats-officedocument.spreadsheetml.sheet;base64,${base64Data}`;

    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};
