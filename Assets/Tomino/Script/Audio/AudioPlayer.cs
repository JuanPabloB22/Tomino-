// =============================================================================
//  AudioPlayer.cs  –  Sistema de Audio Procedural para Tomino
//  Reemplaza completamente el AudioPlayer.cs original.
//  NO requiere archivos WAV/MP3. Todos los sonidos se generan por código.
//
//  Técnicas implementadas:
//    • Ondas básicas : seno, cuadrada, triangular, sierra
//    • Síntesis aditiva  : armónicos sumados con amplitudes independientes
//    • Síntesis FM       : modulador de frecuencia para impactos/errores
//    • Wavetable sweep   : tabla de onda interpolada para rotación de pieza
//    • Glissando         : barrido de frecuencia (hard drop)
//    • LFO               : modulación de amplitud/frecuencia (subida de nivel)
//    • ADSR              : envolvente en todos los sonidos
//    • Variación proc.   : variación aleatoria de pitch en movimiento lateral
//    • Secuenciador proc.: música de fondo generada paso a paso (coroutine)
// =============================================================================

using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Tomino.Audio
{
    public class AudioPlayer : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        //  Constantes globales de síntesis
        // -----------------------------------------------------------------------
        private const int   SR  = 44100;           // sample rate
        private const float TAU = 2f * Mathf.PI;   // 2π reutilizado en cada oscilador

        // -----------------------------------------------------------------------
        //  AudioSources (uno por capa para control de volumen independiente)
        // -----------------------------------------------------------------------
        private AudioSource _sfx;   // efectos de UI e in-game
        private AudioSource _bass;  // bajo de la música de fondo
        private AudioSource _arp;   // arpegio de la música de fondo
        private AudioSource _perc;  // percusión sintética

        // -----------------------------------------------------------------------
        //  Estado de la música
        // -----------------------------------------------------------------------
        private Coroutine _musicCoroutine;
        private int  _musicLevel   = 1;
        private bool _musicEnabled = true;

        // Escala menor pentatónica en C (dos octavas + cima)
        // C, Eb, F, G, Bb  ×2  + C5
        private static readonly float[] Scale = {
            130.81f, 155.56f, 174.61f, 196.00f, 233.08f,   // octava 3
            261.63f, 311.13f, 349.23f, 392.00f, 466.16f,   // octava 4
            523.25f                                          // C5
        };

        // =======================================================================
        //  LIFECYCLE
        // =======================================================================

        internal void Awake()
        {
            _sfx  = MakeSource(0.68f);
            _bass = MakeSource(0.38f);
            _arp  = MakeSource(0.22f);
            _perc = MakeSource(0.28f);
        }

        private AudioSource MakeSource(float vol)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.volume = vol;
            return s;
        }

        // =======================================================================
        //  API PÚBLICA – Efectos de UI
        // =======================================================================

        /// <summary>Pausa del juego. Tono descendente suave.</summary>
        public void PlayPauseClip()
        {
            // Seno con frecuencia media-grave, ADSR con sustain bajo
            Emit(_sfx, Sine(310f, Env(0.005f, 0.07f, 0.15f, 0.09f, 0.18f)));
        }

        /// <summary>Reanudación. Tono ascendente.</summary>
        public void PlayResumeClip()
        {
            Emit(_sfx, Sine(430f, Env(0.005f, 0.05f, 0.25f, 0.06f, 0.15f)));
        }

        /// <summary>Nueva partida. Arpegio ascendente de triángulos.</summary>
        public void PlayNewGameClip()
        {
            ResetMusicLevel();
            StartCoroutine(ArpeggioStart());
            StartCoroutine(RestartMusicAfter(0.45f));
        }

        /// <summary>Toggle ON (configuración).</summary>
        public void PlayToggleOnClip()  => PlayResumeClip();

        /// <summary>Toggle OFF (configuración).</summary>
        public void PlayToggleOffClip() => PlayPauseClip();

        // =======================================================================
        //  API PÚBLICA – Efectos in-game
        // =======================================================================

        /// <summary>
        /// Movimiento lateral. Onda cuadrada corta con variación aleatoria de
        /// pitch ±6 % para evitar fatiga auditiva en acciones repetitivas.
        /// </summary>
        public void PlayPieceMoveClip()
        {
            float freq = 270f * (1f + Random.Range(-0.06f, 0.06f));
            Emit(_sfx, Square(freq, Env(0.002f, 0.022f, 0f, 0.018f, 0.055f)));
        }

        /// <summary>
        /// Rotación de pieza. Wavetable sweep ascendente (tabla: seno + 2°arm + 3°arm).
        /// Sugiere giro y cambio de orientación.
        /// </summary>
        public void PlayPieceRotateClip()
        {
            Emit(_sfx, WavetableSweep(290f, 470f, 0.12f));
        }

        /// <summary>
        /// Fijación de pieza en el tablero. FM grave con ataque medio y decay rápido.
        /// Confirma cierre de la pieza.
        /// </summary>
        public void PlayPieceDropClip()
        {
            Emit(_sfx, FM(175f, 88f, 3.2f, Env(0.004f, 0.075f, 0f, 0.055f, 0.14f)));
        }

        /// <summary>
        /// Hard drop (caída rápida). Glissando descendente en onda sierra.
        /// Refuerza velocidad y peso.
        /// </summary>
        public void PlayHardDropClip()
        {
            Emit(_sfx, Glissando(390f, 65f, 0.18f));
        }

        /// <summary>
        /// Líneas eliminadas. Arpegio aditivo; escala y se intensifica con más líneas.
        /// </summary>
        public void PlayLinesClearedClip(int lines)
        {
            StartCoroutine(ArpeggioLines(lines));
        }

        /// <summary>
        /// Subida de nivel. LFO sweep con tremolo suave – comunica progreso y dificultad.
        /// </summary>
        public void PlayLevelUpClip()
        {
            Emit(_sfx, LFOSweep(220f, 880f, 0.55f, 9f));
            _musicLevel = Mathf.Min(_musicLevel + 1, 10);
        }

        /// <summary>
        /// Movimiento inválido. FM disonante breve, sin saturar la mezcla.
        /// </summary>
        public void PlayInvalidMoveClip()
        {
            Emit(_sfx, FM(108f, 76f, 6.2f, Env(0.003f, 0.04f, 0f, 0.022f, 0.072f), 0.52f));
        }

        /// <summary>
        /// Game over. Secuencia descendente FM + detiene la música.
        /// </summary>
        public void PlayGameOverClip()
        {
            StopMusic();
            StartCoroutine(GameOverSequence());
        }

        // =======================================================================
        //  API PÚBLICA – Música de fondo
        // =======================================================================

        public void StartMusic()
        {
            if (_musicCoroutine != null) StopCoroutine(_musicCoroutine);
            _musicEnabled    = true;
            _musicCoroutine  = StartCoroutine(MusicSequencer());
        }

        public void StopMusic()
        {
            if (_musicCoroutine != null) StopCoroutine(_musicCoroutine);
            _musicCoroutine = null;
        }

        public void SetMusicEnabled(bool on)
        {
            _musicEnabled = on;
            if (!on) StopMusic();
            else if (_musicCoroutine == null) StartMusic();
        }

        public void ResetMusicLevel() => _musicLevel = 1;

        // =======================================================================
        //  COROUTINES DE SECUENCIAS SONORAS
        // =======================================================================

        /// <summary>Arpegio de inicio de partida (triángulos ascendentes).</summary>
        private IEnumerator ArpeggioStart()
        {
            int[] indices = { 0, 2, 4, 5, 7 };
            foreach (int idx in indices)
            {
                Emit(_sfx, Triangle(Scale[idx], Env(0.003f, 0.035f, 0.28f, 0.04f, 0.08f)));
                yield return new WaitForSeconds(0.062f);
            }
        }

        /// <summary>
        /// Arpegio de recompensa al eliminar líneas.
        /// Más líneas → más notas, pitch más alto, mayor satisfacción.
        /// </summary>
        private IEnumerator ArpeggioLines(int lines)
        {
            // lines 1→4 notas base 4→7, duración total ~0.4–0.6 s
            int noteCount = 3 + lines;
            float noteDur = 0.065f;
            int   baseIdx = 3 + lines;       // pitch base escala con las líneas

            for (int i = 0; i < noteCount; i++)
            {
                int   idx  = Mathf.Clamp(baseIdx + i, 0, Scale.Length - 1);
                float freq = Scale[idx];
                float amp  = 0.92f - i * 0.04f;
                // Síntesis aditiva: fundamental + 2° + 3° armónico
                Emit(_sfx, Additive(
                    new[] { freq,       freq * 2f,    freq * 3f   },
                    new[] { amp,        amp * 0.45f,  amp * 0.18f },
                    Env(0.002f, 0.03f, 0.35f, 0.04f, noteDur)
                ));
                yield return new WaitForSeconds(noteDur * 0.72f);
            }
        }

        /// <summary>Secuencia FM descendente para game over.</summary>
        private IEnumerator GameOverSequence()
        {
            int[] idx = { 10, 7, 5, 4, 2, 0 };
            foreach (int i in idx)
            {
                Emit(_sfx, FM(Scale[i], Scale[i] * 0.47f, 4.8f,
                             Env(0.005f, 0.09f, 0.22f, 0.08f, 0.17f)));
                yield return new WaitForSeconds(0.14f);
            }
        }

        /// <summary>Espera y reinicia la música (llamado al iniciar nueva partida).</summary>
        private IEnumerator RestartMusicAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_musicEnabled) StartMusic();
        }

        // =======================================================================
        //  SECUENCIADOR DE MÚSICA DE FONDO
        //
        //  Estructura:  16 pasos = 1 compás 4/4 en corcheas de dieciseisavo
        //  Capas:
        //    Bass  – cuadrada grave (octava -1), cada 1/8 nota
        //    Arp   – seno en escala pentatónica, cada 1/16 nota
        //    Perc  – kick + hihat + snare sintetizados (desde nivel 2)
        //
        //  Con el nivel sube el BPM (115→155) y se añaden capas.
        // =======================================================================

        private IEnumerator MusicSequencer()
        {
            // índices en Scale[] para bajo y arpegio
            int[] bassIdx = { 0, -1, 4, -1,  0, -1, 2, -1,
                              0, -1, 4, -1,  3, -1, 2, -1 };
            int[] arpIdx  = { 0,  2, 4,  5,  4,  2, 5,  7,
                              0,  4, 2,  5,  7,  5, 9,  7 };
            // 0=silencio  1=kick  2=hihat  3=snare
            int[] percType= { 1,  2, 0,  2,  3,  2, 0,  2,
                              1,  2, 0,  2,  3,  2, 0,  2 };

            int step = 0;
            while (true)
            {
                if (!_musicEnabled)
                {
                    yield return new WaitForSeconds(0.05f);
                    continue;
                }

                // BPM escala con el nivel: 115 + 5×(nivel-1), tope en 155
                float bpm     = 115f + Mathf.Clamp(_musicLevel - 1, 0, 8) * 5f;
                float stepSec = 60f / bpm / 4f;   // duración de 1/16 nota en segundos
                int   s       = step % 16;

                // ── Bajo (solo en pasos pares = cada 1/8 nota) ──
                if (s % 2 == 0 && bassIdx[s] >= 0)
                {
                    float bf  = Scale[bassIdx[s]] * 0.5f;      // octava más baja
                    float dur = stepSec * 1.85f;
                    Emit(_bass, Square(bf, Env(0.005f, 0.055f, 0.45f, 0.1f, dur), 0.62f));
                }

                // ── Arpegio (todo el tiempo) ──
                {
                    float af  = Scale[Mathf.Clamp(arpIdx[s], 0, Scale.Length - 1)];
                    float dur = stepSec * 0.82f;
                    Emit(_arp, Sine(af, Env(0.003f, 0.03f, 0.18f, 0.045f, dur)));
                }

                // ── Percusión (a partir de nivel 2) ──
                if (_musicLevel >= 2)
                {
                    switch (percType[s])
                    {
                        case 1: Emit(_perc, Kick (stepSec * 0.48f)); break;
                        case 2: Emit(_perc, Hihat(stepSec * 0.32f)); break;
                        case 3: Emit(_perc, Snare(stepSec * 0.44f)); break;
                    }
                }

                step++;
                yield return new WaitForSeconds(stepSec);
            }
        }

        // =======================================================================
        //  MOTOR DE SÍNTESIS
        //  Todos los métodos son estáticos (funciones puras) y devuelven float[].
        //  El array se convierte en AudioClip y se reproduce con PlayOneShot.
        // =======================================================================

        // ── Helpers básicos ──────────────────────────────────────────────────

        private static int N(float dur) => Mathf.Max(1, Mathf.CeilToInt(dur * SR));

        /// <summary>
        /// Convierte float[] de muestras en AudioClip y lo reproduce con PlayOneShot.
        /// </summary>
        private static void Emit(AudioSource src, float[] buf)
        {
            if (buf == null || buf.Length == 0) return;
            var clip = AudioClip.Create("p", buf.Length, 1, SR, false);
            clip.SetData(buf, 0);
            src.PlayOneShot(clip);
        }

        // ── Envolvente ADSR ──────────────────────────────────────────────────

        /// <summary>
        /// Genera una envolvente ADSR.
        /// a=attack, d=decay, s=sustain(0-1), r=release, dur=duración total en segundos.
        /// </summary>
        private static float[] Env(float a, float d, float s, float r, float dur)
        {
            int n  = N(dur);
            int nA = N(a),  nD = N(d),  nR = N(r);
            int nS = Mathf.Max(0, n - nA - nD - nR);
            float[] e = new float[n];

            for (int i = 0; i < n; i++)
            {
                if (i < nA)
                    e[i] = (float)i / nA;
                else if (i < nA + nD)
                    e[i] = 1f - (1f - s) * (float)(i - nA) / Mathf.Max(1, nD);
                else if (i < nA + nD + nS)
                    e[i] = s;
                else
                {
                    float t = (float)(i - nA - nD - nS) / Mathf.Max(1, nR);
                    e[i] = s * (1f - Mathf.Clamp01(t));
                }
            }
            return e;
        }

        // ── Osciladores básicos ──────────────────────────────────────────────

        /// <summary>Onda seno con envolvente ADSR.</summary>
        private static float[] Sine(float freq, float[] env, float amp = 0.85f)
        {
            float[] b = new float[env.Length];
            for (int i = 0; i < b.Length; i++)
                b[i] = amp * env[i] * Mathf.Sin(TAU * freq * i / SR);
            return b;
        }

        /// <summary>
        /// Onda cuadrada con envolvente ADSR.
        /// Timbre más brillante y penetrante que el seno – ideal para movimiento.
        /// </summary>
        private static float[] Square(float freq, float[] env, float amp = 0.75f)
        {
            float[] b = new float[env.Length];
            for (int i = 0; i < b.Length; i++)
            {
                float v = Mathf.Sin(TAU * freq * i / SR) >= 0f ? 1f : -1f;
                b[i] = amp * env[i] * v;
            }
            return b;
        }

        /// <summary>
        /// Onda triangular con envolvente ADSR.
        /// Más suave que cuadrada, ideal para melodías de interfaz.
        /// </summary>
        private static float[] Triangle(float freq, float[] env, float amp = 0.85f)
        {
            float[] b = new float[env.Length];
            for (int i = 0; i < b.Length; i++)
            {
                float ph = (freq * i / SR) % 1f;
                float v  = ph < 0.5f ? 4f * ph - 1f : 3f - 4f * ph;
                b[i] = amp * env[i] * v;
            }
            return b;
        }

        // ── Síntesis aditiva ─────────────────────────────────────────────────

        /// <summary>
        /// Síntesis aditiva: suma N armónicos con amplitudes independientes.
        /// Usado en eliminación de líneas para construir timbre brillante y recompensante.
        /// </summary>
        private static float[] Additive(float[] freqs, float[] amps, float[] env)
        {
            float[] b = new float[env.Length];
            float   total = 0f;
            foreach (float a in amps) total += a;
            if (total < 1e-6f) total = 1f;

            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / SR;
                float s = 0f;
                for (int h = 0; h < freqs.Length; h++)
                    s += (amps[h] / total) * Mathf.Sin(TAU * freqs[h] * t);
                b[i] = env[i] * s;
            }
            return b;
        }

        // ── Síntesis FM ──────────────────────────────────────────────────────

        /// <summary>
        /// Síntesis FM (frecuency modulation).
        /// carrier = frecuencia portadora, modFreq = frecuencia moduladora,
        /// modIdx = índice de modulación (controla riqueza tímbrica).
        /// Alto modIdx → más armónicos / distorsión (errores, impactos).
        /// </summary>
        private static float[] FM(float carrier, float modFreq, float modIdx,
                                  float[] env, float amp = 0.85f)
        {
            float[] b = new float[env.Length];
            for (int i = 0; i < b.Length; i++)
            {
                float t   = (float)i / SR;
                float mod = modIdx * Mathf.Sin(TAU * modFreq * t);
                b[i] = amp * env[i] * Mathf.Sin(TAU * carrier * t + mod);
            }
            return b;
        }

        // ── Síntesis por tabla de onda (Wavetable) ───────────────────────────

        /// <summary>
        /// Wavetable sweep: oscilador con tabla personalizada (seno + 2°arm + 3°arm),
        /// barriendo frecuencia de f0 a f1. Interpolación lineal entre muestras de tabla.
        /// Usado para rotación de pieza: sugiere giro y cambio de estado.
        /// </summary>
        private static float[] WavetableSweep(float f0, float f1, float dur)
        {
            // Construir wavetable: mezcla de armónicos da timbre suave y brillante
            const int tableLen = 512;
            float[] table = new float[tableLen];
            for (int i = 0; i < tableLen; i++)
            {
                float ph = (float)i / tableLen;
                table[i] = 0.60f * Mathf.Sin(TAU * ph)
                          + 0.28f * Mathf.Sin(TAU * 2f * ph)
                          + 0.12f * Mathf.Sin(TAU * 3f * ph);
            }

            float[] env = Env(0.003f, 0.048f, 0.32f, 0.055f, dur);
            float[] b   = new float[env.Length];
            float   ph2 = 0f;

            for (int i = 0; i < b.Length; i++)
            {
                float t    = (float)i / b.Length;
                float freq = Mathf.Lerp(f0, f1, t * t);   // ease-in para barrido natural

                // Interpolación lineal en la tabla
                float fi  = ph2 * tableLen;
                int   ti0 = (int)fi % tableLen;
                int   ti1 = (ti0 + 1) % tableLen;
                float fr  = fi - Mathf.Floor(fi);
                float s   = table[ti0] * (1f - fr) + table[ti1] * fr;

                b[i] = 0.85f * env[i] * s;
                ph2 += freq / SR;
                if (ph2 >= 1f) ph2 -= Mathf.Floor(ph2);
            }
            return b;
        }

        // ── Glissando (hard drop) ────────────────────────────────────────────

        /// <summary>
        /// Glissando descendente en onda sierra: frecuencia cae de f0 a f1
        /// durante toda la duración. Comunica velocidad y peso de caída rápida.
        /// </summary>
        private static float[] Glissando(float f0, float f1, float dur)
        {
            float[] env = Env(0.005f, dur * 0.62f, 0f, dur * 0.38f, dur);
            float[] b   = new float[env.Length];
            float   ph  = 0f;

            for (int i = 0; i < b.Length; i++)
            {
                float t    = (float)i / b.Length;
                float freq = Mathf.Lerp(f0, f1, t);
                float s    = 2f * ph - 1f;    // sierra

                b[i] = 0.85f * env[i] * s;
                ph  += freq / SR;
                if (ph >= 1f) ph -= 1f;
            }
            return b;
        }

        // ── LFO Sweep (level up) ─────────────────────────────────────────────

        /// <summary>
        /// Barrido de frecuencia con LFO de amplitud.
        /// lfoHz controla la velocidad del tremolo.
        /// Comunica progresión y aumento de dificultad al subir de nivel.
        /// </summary>
        private static float[] LFOSweep(float f0, float f1, float dur, float lfoHz)
        {
            float[] env = Env(0.01f, 0.1f, 0.55f, 0.2f, dur);
            float[] b   = new float[env.Length];
            float   ph  = 0f;

            for (int i = 0; i < b.Length; i++)
            {
                float t    = (float)i / b.Length;
                // LFO modula la amplitud (tremolo)
                float lfo  = 1f + 0.04f * Mathf.Sin(TAU * lfoHz * t * dur);
                float freq = Mathf.Lerp(f0, f1, t);
                float s    = Mathf.Sin(TAU * ph);

                b[i] = 0.85f * env[i] * lfo * s;
                ph  += freq / SR;
                if (ph >= 1f) ph -= Mathf.Floor(ph);
            }
            return b;
        }

        // ── Percusión sintética ──────────────────────────────────────────────

        /// <summary>Kick drum: seno con caída rápida de pitch (180 Hz → 38 Hz).</summary>
        private static float[] Kick(float dur)
        {
            float[] env = Env(0.003f, dur * 0.78f, 0f, dur * 0.22f, dur);
            float[] b   = new float[env.Length];
            float   ph  = 0f;

            for (int i = 0; i < b.Length; i++)
            {
                float t    = (float)i / b.Length;
                float freq = Mathf.Lerp(185f, 38f, t * t);   // caída cuadrática de pitch
                b[i] = 0.90f * env[i] * Mathf.Sin(TAU * ph);
                ph  += freq / SR;
                if (ph >= 1f) ph -= 1f;
            }
            return b;
        }

        /// <summary>
        /// Hi-hat: ruido blanco con filtro paso-altos de un polo.
        /// El filtro simple (y = x - 0.88×x_prev) simula el comportamiento
        /// de un hi-hat cerrado real.
        /// </summary>
        private static float[] Hihat(float dur)
        {
            float[] env  = Env(0.002f, dur * 0.38f, 0f, dur * 0.62f, dur);
            float[] b    = new float[env.Length];
            float   prev = 0f;

            for (int i = 0; i < b.Length; i++)
            {
                float noise = Random.Range(-1f, 1f);
                float hp    = noise - prev * 0.88f;   // paso-altos 1 polo
                prev = noise;
                b[i] = 0.44f * env[i] * hp;
            }
            return b;
        }

        /// <summary>Snare: mezcla 50 % tono (seno 200 Hz) + 50 % ruido blanco.</summary>
        private static float[] Snare(float dur)
        {
            float[] env = Env(0.003f, dur * 0.42f, 0f, dur * 0.58f, dur);
            float[] b   = new float[env.Length];

            for (int i = 0; i < b.Length; i++)
            {
                float t     = (float)i / SR;
                float tone  = 0.5f * Mathf.Sin(TAU * 200f * t);
                float noise = 0.5f * Random.Range(-1f, 1f);
                b[i] = 0.70f * env[i] * (tone + noise);
            }
            return b;
        }
    }
}