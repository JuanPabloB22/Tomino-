// =============================================================================
//  GameController.cs  –  Controlador principal del juego (modificado)
//
//  CAMBIOS respecto al original:
//    • Se conectan los 4 eventos nuevos de Game.cs al AudioPlayer procedural:
//        LinesClearedEvent, LevelUpEvent, HardDropEvent, InvalidMoveEvent
//    • Se llama audioPlayer.StartMusic() al iniciar la partida
//    • Se llama audioPlayer.PlayGameOverClip() al terminar la partida
//    • Se llama audioPlayer.SetMusicEnabled() cuando cambia la configuración
//    • El musicAudioSource original se desactiva para evitar duplicidad de audio
//    • El botón "Jugar de nuevo" reinicia el nivel musical antes de empezar
// =============================================================================

using Tomino.Audio;
using Tomino.Input;
using Tomino.Model;
using Tomino.View;
using UnityEngine;

namespace Tomino
{
    public class GameController : MonoBehaviour
    {
        // ── Referencias asignadas en el Inspector ────────────────────────────
        public GameConfig  gameConfig;
        public AlertView   alertView;
        public SettingsView settingsView;
        public AudioPlayer audioPlayer;
        public GameObject  screenButtons;

        /// <summary>
        /// AudioSource original de música de Tomino.
        /// Se mantiene para compatibilidad con el Inspector, pero se desactiva
        /// porque la música la genera audioPlayer de forma procedural.
        /// </summary>
        public AudioSource musicAudioSource;

        // ── Estado interno ───────────────────────────────────────────────────
        private Game          _game;
        private UniversalInput _universalInput;

        // ── Lifecycle ────────────────────────────────────────────────────────

        internal void Awake()
        {
            Application.targetFrameRate = 60;
            HandlePlayerSettings();
            Settings.changedEvent += HandlePlayerSettings;
        }

        internal void Start()
        {
            Board board = new(10, 20);

            gameConfig.boardView.SetBoard(board);
            gameConfig.nextPieceView.SetBoard(board);

            _universalInput = new UniversalInput(
                new KeyboardInput(),
                gameConfig.boardView.touchInput
            );

            _game = new Game(board, _universalInput);

            // ── Eventos originales ────────────────────────────────────────
            _game.FinishedEvent             += OnGameFinished;
            _game.PieceFinishedFallingEvent += audioPlayer.PlayPieceDropClip;
            _game.PieceRotatedEvent         += audioPlayer.PlayPieceRotateClip;
            _game.PieceMovedEvent           += audioPlayer.PlayPieceMoveClip;

            // ── Eventos nuevos (audio procedural) ─────────────────────────
            _game.LinesClearedEvent         += audioPlayer.PlayLinesClearedClip;
            _game.LevelUpEvent              += audioPlayer.PlayLevelUpClip;
            _game.HardDropEvent             += audioPlayer.PlayHardDropClip;
            _game.InvalidMoveEvent          += audioPlayer.PlayInvalidMoveClip;

            // ── Iniciar partida y música ──────────────────────────────────
            _game.Start();
            audioPlayer.StartMusic();

            gameConfig.scoreView.game = _game;
            gameConfig.levelView.game = _game;
        }

        internal void Update()
        {
            _game.Update(Time.deltaTime);
        }

        // ── Botones de pantalla ──────────────────────────────────────────────

        public void OnPauseButtonTap()
        {
            _game.Pause();
            audioPlayer.PlayPauseClip();
            ShowPauseView();
        }

        public void OnMoveLeftButtonTap()  => _game.SetNextAction(PlayerAction.MoveLeft);
        public void OnMoveRightButtonTap() => _game.SetNextAction(PlayerAction.MoveRight);
        public void OnMoveDownButtonTap()  => _game.SetNextAction(PlayerAction.MoveDown);
        public void OnRotateButtonTap()    => _game.SetNextAction(PlayerAction.Rotate);

        // ── Callbacks de partida ─────────────────────────────────────────────

        private void OnGameFinished()
        {
            // Reproducir sonido de game over (incluye StopMusic internamente)
            audioPlayer.PlayGameOverClip();

            alertView.SetTitle(TextID.GameFinished);
            // Al pulsar "Jugar de nuevo": reiniciar nivel musical + iniciar juego
            alertView.AddButton(TextID.PlayAgain,
                () => { _game.Start(); audioPlayer.PlayNewGameClip(); },
                audioPlayer.PlayNewGameClip);
            alertView.Show();
        }

        private void ShowPauseView()
        {
            alertView.SetTitle(TextID.GamePaused);
            alertView.AddButton(TextID.Resume,
                _game.Resume,
                audioPlayer.PlayResumeClip);
            alertView.AddButton(TextID.NewGame,
                () => { _game.Start(); audioPlayer.PlayNewGameClip(); },
                audioPlayer.PlayNewGameClip);
            alertView.AddButton(TextID.Settings,
                ShowSettingsView,
                audioPlayer.PlayResumeClip);
            alertView.Show();
        }

        private void ShowSettingsView()
        {
            settingsView.Show(ShowPauseView);
        }

        // ── Configuración ────────────────────────────────────────────────────

        private void HandlePlayerSettings()
        {
            screenButtons.SetActive(Settings.ScreenButtonsEnabled);
            gameConfig.boardView.touchInput.Enabled = !Settings.ScreenButtonsEnabled;

            // Desactivar AudioSource original de Tomino para evitar duplicidad
            if (musicAudioSource != null)
                musicAudioSource.gameObject.SetActive(false);

            // Controlar música procedural con el ajuste del jugador
            audioPlayer.SetMusicEnabled(Settings.MusicEnabled);
        }
    }
}