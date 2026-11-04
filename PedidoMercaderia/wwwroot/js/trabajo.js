let pendientes = false;
window.pedidoPendiente = valor => { pendientes = valor; };
window.addEventListener('beforeunload', e => {
    if (pendientes) { e.preventDefault(); e.returnValue = ''; }
});
