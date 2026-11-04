const paneles = new WeakMap();

export function mostrar(panel, anclaId, receptor) {
    destruir(panel);
    const ancla = document.getElementById(anclaId);
    if (!ancla) return;
    const posicionar = () => {
        if (!panel.matches(':popover-open')) return;
        const margen = 12;
        const ancho = document.documentElement.clientWidth;
        const alto = window.innerHeight;
        const rect = ancla.getBoundingClientRect();
        if (ancho <= 640) {
            panel.style.cssText = `left:${margen}px;top:auto;bottom:${margen}px;width:${ancho - margen * 2}px;max-height:${alto - margen * 2}px`;
            return;
        }
        const espacioAbajo = alto - rect.bottom - margen - 6;
        const arriba = espacioAbajo < Math.min(420, panel.scrollHeight) && rect.top > espacioAbajo;
        const disponible = Math.max(160, arriba ? rect.top - margen - 6 : espacioAbajo);
        const anchoPanel = panel.classList.contains('selector-popover') ? Math.min(480, Math.max(260, rect.width)) : 340;
        panel.style.width = `${anchoPanel}px`;
        panel.style.maxHeight = `${Math.min(520, disponible)}px`;
        panel.style.left = `${Math.max(margen, Math.min(rect.left, ancho - anchoPanel - margen))}px`;
        panel.style.bottom = 'auto';
        panel.style.top = `${arriba ? Math.max(margen, rect.top - panel.offsetHeight - 6) : Math.max(margen, rect.bottom + 6)}px`;
    };
    const alCambiar = event => {
        if (event.newState === 'closed') {
            destruir(panel);
            receptor.invokeMethodAsync('Cerrado');
        }
    };
    const alTeclear = event => {
        if (event.target.matches('[data-autofocus]') && ['ArrowDown', 'ArrowUp', 'Enter'].includes(event.key)) {
            event.preventDefault();
        }
    };
    panel.addEventListener('keydown', alTeclear);
    panel.addEventListener('toggle', alCambiar);
    window.addEventListener('resize', posicionar);
    window.addEventListener('scroll', posicionar, true);
    const observador = new ResizeObserver(posicionar);
    observador.observe(panel);
    paneles.set(panel, { ancla, alCambiar, alTeclear, posicionar, observador });
    if (!panel.matches(':popover-open')) panel.showPopover({ source: ancla });
    posicionar();
    (panel.querySelector('[data-autofocus]') || panel).focus({ preventScroll: true });
}

export function mostrarOpcion(id) {
    const opcion = id && document.getElementById(id);
    const lista = opcion?.closest('[role="listbox"]');
    if (!lista) return;
    const item = opcion.getBoundingClientRect(), contenedor = lista.getBoundingClientRect();
    if (item.bottom > contenedor.bottom) lista.scrollTop += item.bottom - contenedor.bottom;
    else if (item.top < contenedor.top) lista.scrollTop -= contenedor.top - item.top;
}

export function ocultar(panel) {
    const ancla = paneles.get(panel)?.ancla;
    destruir(panel);
    if (panel.matches(':popover-open')) panel.hidePopover();
    ancla?.focus({ preventScroll: true });
}

export function destruir(panel) {
    const estado = paneles.get(panel);
    if (!estado) return;
    panel.removeEventListener('toggle', estado.alCambiar);
    panel.removeEventListener('keydown', estado.alTeclear);
    window.removeEventListener('resize', estado.posicionar);
    window.removeEventListener('scroll', estado.posicionar, true);
    estado.observador.disconnect();
    paneles.delete(panel);
}
