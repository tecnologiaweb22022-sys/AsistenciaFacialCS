# Control de asistencia con reconocimiento facial — C# / Windows Forms (IAR-600)

Aplicación de escritorio **Windows Forms (.NET 8, C#)** con **servidor web local integrado**. Los estudiantes **registran su rostro una vez**. Desde entonces, cada vez que llegan, **la cámara los reconoce y marca su asistencia** (puntual o atraso). Funciona **sin internet**, con un router WiFi.

| Dónde | Qué se hace |
|---|---|
| **Programa de escritorio** (laptop del docente) | Cámara en vivo con los nombres, lista del día, registro de rostros, estudiantes, reportes, configuración, cartel QR. |
| **Cámara del aula** | Marca sola al reconocer el rostro (pide 3 cuadros seguidos para confirmar). Avisa si aparece un rostro no registrado. |
| **Celular del estudiante** (`/marcar`) | Marca con una **selfie + su código**: el servidor verifica que la cara sea de ese estudiante. |
| **Celular del estudiante** (`/mi-asistencia`) | Consulta su historial y porcentaje. |
| **Vista pública** (`/`) | Reloj, horario y quién ya llegó (opcional). |
| **Panel web del docente** (`/admin`, con PIN) | Lista del día, correcciones, registrar rostros con fotos del celular, reportes, CSV, alertas, cámara en vivo. |

```
 ESTUDIANTE    CÁMARA / SELFIE     IMAGEN → YuNet (¿dónde hay rostros?) → alinear 112×112 → SFace (vector de 128 números)
 llega al  ──▶ webcam, celular ──▶  → comparar con la GALERÍA (similitud coseno ≥ umbral) → confirmar → DECISIÓN
 aula          o selfie web          → base de datos SQLite: puntual / atraso → ventana, web, alertas, reportes
```

---

## 1. Qué necesitas

| Elemento | Detalle |
|---|---|
| Laptop | Windows 10/11 de 64 bits, 4 GB de RAM. |
| Para compilar | **Visual Studio 2022 o 2026 Community** con la carga **"Desarrollo de escritorio de .NET"**, o solo el **.NET 8 SDK** (o más nuevo). |
| Internet | Solo la **primera vez que se compila** (descarga los paquetes NuGet). Después funciona sin internet. |
| Cámara | Webcam, cámara USB o celular Android como cámara IP. Va en la entrada del aula, a la altura de la cara. |
| Router WiFi | Cualquiera, sin internet. |

Paquetes NuGet (se descargan solos): `OpenCvSharp4.Windows` y `OpenCvSharp4.Extensions` 4.11, `Microsoft.Data.Sqlite`, `QRCoder`.

Los modelos ya vienen en la carpeta `modelos/`:
- **YuNet**: detecta rostros y 5 puntos de la cara. Pesa 0,2 MB. Licencia MIT.
- **SFace**: convierte una cara en un vector de 128 números. Pesa 37 MB. Licencia Apache 2.0.

---

## 2. Antes de compilar: el modelo SFace (37 MB)

El modelo de reconocimiento es grande y viene **aparte** del código. Elige una opción:
- **A (con internet):** doble clic en **`descargar_modelo_sface.bat`**.
- **B (sin internet):** copia **`sface.parte1`** y **`sface.parte2`** en la carpeta `AsistenciaFacialCS` y ejecuta **`unir_modelo_sface.bat`**.

Debe quedar el archivo `AsistenciaFacial\modelos\face_recognition_sface_2021dec.onnx`. El modelo YuNet ya está incluido.

## 2b. Compilar y ejecutar

**Visual Studio**
1. Descomprime la carpeta (por ejemplo, `C:\AsistenciaFacialCS`).
2. Abre `AsistenciaFacial.sln`.
3. Espera a que termine de restaurar los paquetes NuGet.
4. Pulsa **F5**.

La primera vez, Windows pregunta por el acceso a la red: marca **Redes privadas** y acepta. Así pueden entrar los celulares.

**Sin Visual Studio:** instala el .NET 8 SDK y haz doble clic en **`1_ejecutar.bat`**.

**Para otras laptops del aula:** ejecuta **`2_publicar_portable.bat`**. Copia la carpeta `publicado\` a la otra laptop y abre `AsistenciaFacial.exe`. No hace falta instalar .NET.

---

## 2c. Si la cámara no funciona

Pulsa el botón **Diagnóstico** (debajo de la cámara). Prueba las cámaras 0 a 3 con los tres motores de Windows (DirectShow, Media Foundation y automático) y dice cuál **FUNCIONA**. El informe se puede copiar.

| Lo que dice el diagnóstico | Solución |
|---|---|
| **No se pudo cargar OpenCV** (no funciona nada, ni las fotos) | Ejecuta **`instalar_visual_cpp.bat`** (Visual C++ 2015-2022 x64) y reinicia. En Windows edición **N**: instala el *Media Feature Pack*. |
| Ninguna cámara «no abre» | Windows → Privacidad y seguridad → Cámara: activa **Permitir que las aplicaciones de escritorio accedan a la cámara**. Revisa la tecla o tapa de la cámara. |
| «Abre pero NO entrega imágenes» | Otra aplicación la está usando (Zoom, Meet, Teams, WhatsApp, Cámara): ciérrala. |
| Funciona con otro número | Escribe ese número en «Cámara» (ej. `1`). |

El programa ya prueba solo los tres motores y, si la cámara no acepta 1280×720, usa su resolución normal.

## 3. Primer uso, paso a paso

1. **Configuración**: escribe la institución, materia y docente. Define la **hora de entrada**, la **tolerancia** (ejemplo: 10 min, después es atraso) y la **hora de cierre**. Cambia el **PIN**.
2. **Estudiantes**: agrégalos uno por uno o **importa un CSV** con las columnas `codigo;nombres;apellidos;curso` (puedes exportarlo desde Excel).
3. **Registrar rostro** (botón en Estudiantes): el estudiante se para frente a la cámara y gira un poco la cabeza. El programa toma **8 muestras** solo:
   - pide que haya una sola persona;
   - pide que el rostro sea lo bastante grande;
   - descarta las muestras repetidas;
   - avisa si la cara se parece a la de **otro** estudiante ya registrado.

   **Alternativa desde la web:** en `/admin` → Estudiantes → **📷 Fotos**, toma de 3 a 8 fotos con el celular, una persona por foto.
4. **Iniciar la cámara**: desde ese momento, quien llega y mira a la cámara queda marcado. El programa muestra:
   - un recuadro **verde** si es puntual;
   - un recuadro **amarillo** si llega con atraso o mientras confirma;
   - un recuadro **rojo** si es desconocido;
   - abajo, una tarjeta grande con la foto, el nombre y la hora.
5. **Sin cámara**: usa el botón **Probar con fotos…** y elige una carpeta con fotos. Se muestran una tras otra, como si las personas pasaran frente a la cámara.

---

## 4. Marcar desde el celular (selfie)

1. Imprime el **Cartel QR** (botón en el programa o `/admin/qr`) y pégalo en la puerta. Tiene tres códigos:
   - **①** Conectarse al WiFi.
   - **②** Marcar con selfie (`/marcar`).
   - Consultar *mi asistencia*.
2. El estudiante escribe su **código**, toma la **selfie** y la envía. El servidor hace todo esto:
   - detecta el rostro;
   - calcula su vector;
   - lo compara **solo con el rostro registrado de ese código** (verificación 1 a 1);
   - si coincide, marca la asistencia con origen **«celular»**.
3. Si la selfie no coincide con el código, **no se marca**. Al docente le llega una **alerta**.
4. Hay un límite de **6 intentos por minuto** por celular.
5. Las opciones se cambian en **Configuración**:
   - desactivar la marcación por selfie;
   - quitar el código (entonces se busca entre todos los estudiantes: 1 a N).

> La cámara de la página funciona en la red local **sin https**, porque usa la cámara del celular como «subir foto».

---

## 5. Estados, correcciones y reportes

| Estado | Cuándo |
|---|---|
| 🟢 **Puntual** | Llegó hasta *hora de entrada + tolerancia*. |
| 🟠 **Atraso** | Llegó después de la tolerancia y antes del cierre. |
| 🔵 **Justificado** | Lo puso el docente, con un motivo. |
| ⚪ **Falta** | No marcó. |

- **Una marca por estudiante por día.** Si vuelve a pasar, la cámara muestra «ya marcó 07:58».
- Después de la hora de cierre ya no se marca.
- **Corregir**: en la lista del día, elige al estudiante y pulsa Puntual, Atraso, **Justificar…** o Quitar marca. También se puede desde el panel web.
- **Reportes**: elige un rango de fechas. Muestra días de clase, puntuales, atrasos, justificados, faltas y **% de asistencia**. Se exporta a CSV para Excel:
  - `reporte.csv`
  - `asistencias.csv`
  - `estudiantes.csv`
  - `eventos.csv`

---

## 6. Red local (sin internet)

1. Conecta la laptop al router.
2. La barra inferior del programa muestra la dirección, por ejemplo `http://192.168.0.100:5000`.
3. En el router, **reserva esa IP** para la laptop. Así los QR impresos siguen sirviendo.
4. Si un celular no abre la página, revisa:
   - ejecuta `abrir_firewall_windows.bat` **como administrador**;
   - el celular debe estar en el mismo WiFi y sin datos móviles;
   - el router no debe tener «AP isolation».

---

## 7. Ajustes de reconocimiento (`config.json` o ventana Configuración)

| Ajuste | Qué hace |
|---|---|
| `umbral_similitud` (0,42) | Más alto exige más parecido: menos errores, pero puede no reconocer con mala luz. SFace usa 0,363 como referencia. |
| `margen_minimo` (0,05) | La mejor coincidencia debe ganarle por este margen a la segunda. Evita confundir a dos personas parecidas. |
| `confirmaciones` (3) | Cuadros seguidos con la misma identidad antes de marcar. |
| `rostro_minimo_px` (60) | Ignora los rostros muy lejanos. |
| `muestras_registro` (8) | Fotos que se toman al registrar un rostro. |
| `marcar_desde_celular`, `celular_pide_codigo` | Opciones de la selfie. |
| `lista_publica` | Si la página pública muestra los nombres de quienes llegaron. |

---

## 8. Privacidad y límites (importante explicarlo en clase)

- **Qué se guarda.** No se guardan fotos completas, solo **vectores de 128 números** y un recorte de 112×112 para reconocer al estudiante en la lista. Los **rostros desconocidos** sí se guardan como foto en `datos/desconocidos/` para que el docente revise. Bórralos de vez en cuando.
- **Consentimiento.** Pide el consentimiento del estudiante antes de registrar su rostro. Existen botones **Borrar rostro** y **Eliminar estudiante**, que borra todos sus datos.
- **Límite: fotos de fotos.** El sistema **no detecta si le muestran una foto impresa o una pantalla** (no tiene prueba de "vida"). Por eso conviene que el docente vea la cámara o el cartel de última marca. Es un buen tema para discutir: *¿cómo lo haría un sistema comercial?* Algunas respuestas: parpadeo, profundidad, cámara infrarroja.
- **Luz.** La luz influye mucho. Coloca la cámara de frente a los estudiantes, no a contraluz.

---

## 9. Para la clase: qué tecnología hace qué

| Parte | Tipo |
|---|---|
| **YuNet** (`DetectorRostros`) | **Deep Learning, red convolucional (CNN)**: detección de objetos, en este caso rostros, con 5 puntos de la cara. |
| Alineación 112×112 | **Visión clásica**: transformación geométrica de semejanza (rotar, escalar y trasladar). |
| **SFace** (`ReconocedorRostros`) | **Deep Learning**: red entrenada con millones de caras que produce un *embedding* (vector). |
| Similitud coseno + umbral | **Decisión**: comparar vectores. Es *one-shot learning*: no se reentrena la red, solo se guardan vectores. |
| Confirmación de 3 cuadros | Filtro temporal: menos falsos positivos. |
| Base de datos, reportes, web | **Sistema de información y automatización**. |

---

## 10. Archivos

```
AsistenciaFacialCS/
├── AsistenciaFacial.sln · 1_ejecutar.bat · 2_publicar_portable.bat · abrir_firewall_windows.bat
└── AsistenciaFacial/
    ├── Nucleo/   Configuracion · Modelos · BaseDatos (SQLite) · Rostros (YuNet, SFace, galería, fotos web)
    │             MotorAsistencia (cámara: reconocer / registrar) · ServidorWeb (Kestrel) · Utilidades (IP, QR)
    ├── Interfaz/ FormPrincipal · FormEstudiantes · FormRegistroRostro · FormReportes · FormConfiguracion · FormQR · Tema
    ├── wwwroot/  publico · marcar · mi_asistencia · admin · login  (sin CDN, funciona sin internet)
    ├── modelos/  face_detection_yunet_2023mar.onnx · face_recognition_sface_2021dec.onnx
    └── config.json
```

Junto al `.exe` se crea `datos\asistencia.db`, que es la base de datos. Se puede abrir con *DB Browser for SQLite*.
