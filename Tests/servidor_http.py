"""Prueba HTTP sobre un servidor aislado; nunca usar contra datos reales."""
import http.cookiejar, json, sys, urllib.request, urllib.error, pathlib

base, carpeta = sys.argv[1:]
class Cliente:
    def __init__(self):
        self.http = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.token = None
    def pedir(self, ruta, datos=None, metodo=None, csrf=True):
        headers = {}
        if datos is not None:
            headers['Content-Type'] = 'application/json'
            if csrf:
                if not self.token: self.token = self.pedir('/api/auth/token')[1]['token']
                headers['X-CSRF-TOKEN'] = self.token
        req = urllib.request.Request(base + ruta, data=None if datos is None else json.dumps(datos).encode(), method=metodo, headers=headers)
        try: respuesta = self.http.open(req)
        except urllib.error.HTTPError as e: respuesta = e
        raw = respuesta.read().decode()
        try: contenido = json.loads(raw) if raw else None
        except json.JSONDecodeError: contenido = raw
        return respuesta.code, contenido

n = 0
def verificar(condicion, texto):
    global n
    assert condicion, texto
    n += 1
    print('OK:', texto)

admin, ana = Cliente(), Cliente()
verificar(admin.pedir('/')[0] == 200, 'Servidor entrega la aplicación')
verificar(admin.pedir('/js/trabajo.js')[0] == 200, 'Servidor entrega el módulo de aviso de cambios pendientes')
verificar(admin.pedir('/api/trabajo/')[0] == 401, 'Inventario y pedidos requieren iniciar sesión')
verificar(admin.pedir('/api/auth/sesion')[1]['requiereConfiguracion'], 'Instalación de pruebas sin usuarios')
clave = pathlib.Path(carpeta, 'clave-instalacion.txt').read_text().strip()
cuenta = dict(usuario='admin', nombre='Administrador de prueba', clave='Prueba-clave-2026', claveInstalacion=clave)
verificar(admin.pedir('/api/auth/configurar', cuenta, csrf=False)[0] == 400, 'Rechaza cambios sin protección CSRF')
verificar(admin.pedir('/api/auth/configurar', cuenta)[0] == 200, 'Crea administrador con clave de instalación')
verificar(admin.pedir('/api/auth/entrar', cuenta)[0] == 200, 'Inicia sesión con contraseña')
admin.token = None
verificar(admin.pedir('/api/auth/sesion')[1]['usuario']['rol'] == 'Administrador', 'La cookie identifica la sesión')
verificar(admin.pedir('/api/usuarios', dict(usuario='ana', nombre='Ana prueba', clave='Prueba-clave-2026'))[0] == 200, 'Administrador crea colaborador')
verificar(ana.pedir('/api/auth/entrar', dict(usuario='ana', clave='Prueba-clave-2026'))[0] == 200, 'Segundo usuario inicia sesión')
ana.token = None
verificar(ana.pedir('/api/usuarios')[0] == 403, 'Colaborador no administra usuarios')

def producto(codigo, stock):
    return dict(codigo=codigo, descripcion='Producto de prueba ' + codigo, marca='Prueba', existencias={'B4':stock,'B1':50,'B40':100})
def importar(fecha, productos):
    estado = admin.pedir('/api/trabajo/')[1]
    return admin.pedir('/api/trabajo/inventarios', dict(revision=estado['revision'], fecha=fecha, inventario=dict(nombreArchivo='prueba.xlsx', productos=productos, bodegasDetectadas=['B4','B1','B40'])))

verificar(importar('2026-10-01', [producto('CABLE',10), producto('IGUAL',5)])[0] == 200, 'Importa primer corte en base persistente')
estado = admin.pedir('/api/trabajo/')[1]
config = estado['configuracion']
config.update(bodegaLocal='B4', bodegasOrigen=['B1','B40'])
verificar(admin.pedir('/api/trabajo/configuracion', dict(revision=estado['revision'], configuracion=config), 'PUT')[0] == 200, 'Configura sucursal y centros detectados')
verificar(importar('2026-10-08', [producto('CABLE',4)])[0] == 400, 'Bloquea reemplazar un inventario abierto')
estado = admin.pedir('/api/trabajo/')[1]
verificar(admin.pedir('/api/trabajo/anular-preparacion', dict(revision=estado['revision']))[0] == 200, 'Permite otra carga tras anular explícitamente')
verificar(importar('2026-10-08', [producto('CABLE',4), producto('IGUAL',5), producto('NUEVO',0)])[0] == 200, 'Importa segunda semana')
estado = ana.pedir('/api/trabajo/')[1]
verificar({p['codigo'] for p in estado['propuestas']} == {'CABLE','NUEVO'}, 'Distingue bajadas y novedades sin recomendar stock estable')
rev = estado['revision']
verificar(ana.pedir('/api/trabajo/generar', dict(revision=rev, incluirNuevos=False))[0] == 200, 'Genera borrador automático desde una cuenta colaboradora')
verificar(admin.pedir('/api/trabajo/borrador', dict(revision=rev, corteId=estado['corte']['id'], items=[]), 'PUT')[0] == 409, 'Rechaza sobrescritura desde otro usuario con datos antiguos')
estado = admin.pedir('/api/trabajo/')[1]
verificar(len(estado['borrador']) == 1 and estado['borrador'][0]['cantidadSolicitada'] == 6, 'Otro usuario ve la cantidad sugerida compartida')
verificar(admin.pedir('/api/trabajo/realizar', dict(revision=estado['revision']))[0] == 200, 'Registra el pedido realizado')
estado = ana.pedir('/api/trabajo/')[1]
verificar(estado['historial'][0]['estado'] == 'Guardado' and not estado['borrador'], 'Historial compartido y nuevo borrador vacío')
verificar(not any(p['codigo']=='CABLE' for p in estado['propuestas']), 'El pendiente no se pide automáticamente otra vez')
verificar(ana.pedir('/api/auth/salir', {})[0] == 200 and ana.pedir('/api/trabajo/')[0] == 401, 'Cerrar sesión revoca el acceso del navegador')
verificar(ana.pedir('/api/trabajo/historial')[0] == 401, 'El historial filtrado también requiere sesión')
pagina = admin.pedir('/api/trabajo/historial?codigo=cable&estado=Guardado')[1]
verificar(pagina['total'] == 1 and pagina['pedidos'][0]['id'] == estado['historial'][0]['id'], 'Busca pedidos por código y estado desde la API')
id_primero = estado['historial'][0]['id']
verificar(importar('2026-10-09', [producto('CABLE',4), producto('IGUAL',5), producto('NUEVO',0)])[0] == 200, 'Un pedido guardado permite un Excel nuevo sin recepción')
estado = admin.pedir('/api/trabajo/')[1]
verificar(admin.pedir('/api/trabajo/borrador', dict(revision=estado['revision'], corteId=estado['corte']['id'], items=[dict(codigoProducto='CABLE', bodegaOrigen='B1', cantidadSolicitada=2)]), 'PUT')[0] == 200, 'Guarda una segunda solicitud del mismo código')
estado = admin.pedir('/api/trabajo/')[1]
verificar(admin.pedir('/api/trabajo/realizar', dict(revision=estado['revision']))[0] == 200, 'Conserva un segundo pedido independiente en el historial')
estado = admin.pedir('/api/trabajo/')[1]
segundo = estado['historial'][0]['id']

sin_centro = producto('CAMARA_NUEVA', 0)
sin_centro['existencias'] = {'B4':0,'B1':0,'B40':0,'B39':5000}
verificar(importar('2026-10-15', [producto('CABLE',1), producto('IGUAL',0), producto('NUEVO',0), sin_centro])[0] == 200, 'Compara una tercera semana con agotados, bajadas y novedades')
estado = admin.pedir('/api/trabajo/')[1]
verificar(estado['masPedidos'][0]['codigo'] == 'CABLE' and estado['masPedidos'][0]['pedidos'] == 2 and estado['masPedidos'][0]['cantidad'] == 8, 'La API calcula frecuencia y cantidad histórica')
verificar(estado['propuestas'][0]['codigo'] == 'IGUAL' and estado['propuestas'][0]['tipo'] == 'Agotado', 'El nuevo agotamiento recibe prioridad')
verificar(any(c['codigo']=='CAMARA_NUEVA' and c['tipo']=='Nuevo' for c in estado['cambios']) and not any(p['codigo']=='CAMARA_NUEVA' for p in estado['propuestas']), 'Informa novedades aunque no se puedan pedir desde centros autorizados')
verificar(admin.pedir('/api/trabajo/historial?desde=2026-10-20&hasta=2026-10-01')[0] == 400, 'Rechaza rangos de fecha invertidos')
print(f'{n} comprobaciones HTTP correctas.')
