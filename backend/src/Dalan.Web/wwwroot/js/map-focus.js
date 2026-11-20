(() => {
    window.dalanMapFocus = (root, map, canvas, hint) => {
        const text = 'Click the map or press Enter to enable wheel zoom. Escape or click outside to return to page scrolling.';
        const reportZoom = () => { root.dataset.mapZoom = map.getZoom().toFixed(3); };
        map.on('zoomend', reportZoom); reportZoom();
        function unlock() {
            if(root.dataset.mapLocked === 'true') map.stop();
            map.scrollZoom.disable(); root.dataset.mapLocked = 'false'; canvas.classList.remove('map-locked');
            if(hint) hint.textContent=text;
        }
        function lock() {
            map.scrollZoom.enable(); root.dataset.mapLocked = 'true'; canvas.classList.add('map-locked');
            if(hint) hint.textContent='Map focused · scroll to zoom. Press Escape or click outside to release.';
        }
        canvas.addEventListener('pointerdown',lock,true);
        canvas.addEventListener('keydown',event=>{if(event.key==='Enter'){lock();map.getCanvas().focus({preventScroll:true});event.preventDefault();}});
        document.addEventListener('pointerdown',event=>{if(!canvas.contains(event.target))unlock();});
        document.addEventListener('keydown',event=>{if(event.key==='Escape'&&root.dataset.mapLocked==='true'){unlock();canvas.focus({preventScroll:true});event.preventDefault();}});
        unlock();
    };
})();
