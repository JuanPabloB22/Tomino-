// =============================================================================
//  Game.cs  –  Lógica principal del juego (modificado para audio procedural)
//
//  CAMBIOS respecto al original:
//    + LinesClearedEvent  : se dispara con el número de líneas eliminadas (1-4)
//    + LevelUpEvent       : se dispara cuando el jugador sube de nivel
//    + HardDropEvent      : se dispara en caída rápida (PlayerAction.Fall)
//    + InvalidMoveEvent   : se dispara cuando MoveLeft/MoveRight falla
//
//  El resto de la lógica es idéntica al archivo original de Tomino.
// =============================================================================

using Tomino.Input;
using Tomino.Model;

namespace Tomino
{
    /// <summary>
    /// Controls the game logic by handling user input and updating the board state.
    /// </summary>
    public class Game
    {
        // ── Delegados ────────────────────────────────────────────────────────

        public delegate void GameEventHandler();

        /// <summary>Delegado que transporta el conteo de líneas eliminadas.</summary>
        public delegate void LinesEventHandler(int count);

        // ── Eventos originales ───────────────────────────────────────────────

        /// <summary>The event triggered when the game is finished.</summary>
        public event GameEventHandler FinishedEvent = delegate { };

        /// <summary>The event triggered when the piece is moved.</summary>
        public event GameEventHandler PieceMovedEvent = delegate { };

        /// <summary>The event triggered when the piece is rotated.</summary>
        public event GameEventHandler PieceRotatedEvent = delegate { };

        /// <summary>The event triggered when the piece finishes falling.</summary>
        public event GameEventHandler PieceFinishedFallingEvent = delegate { };

        // ── Eventos nuevos (audio procedural) ───────────────────────────────

        /// <summary>
        /// Se dispara cuando se eliminan líneas completas.
        /// El parámetro indica cuántas líneas se eliminaron (1-4).
        /// </summary>
        public event LinesEventHandler LinesClearedEvent = delegate { };

        /// <summary>Se dispara cuando el jugador sube de nivel.</summary>
        public event GameEventHandler LevelUpEvent = delegate { };

        /// <summary>Se dispara en hard drop (PlayerAction.Fall).</summary>
        public event GameEventHandler HardDropEvent = delegate { };

        /// <summary>
        /// Se dispara cuando un movimiento lateral no puede ejecutarse
        /// (pieza bloqueada por el borde o por otra pieza).
        /// </summary>
        public event GameEventHandler InvalidMoveEvent = delegate { };

        // ── Estado ───────────────────────────────────────────────────────────

        /// <summary>The current score.</summary>
        public Score Score { get; private set; }

        /// <summary>The current level.</summary>
        public Level Level { get; private set; }

        private readonly Board         _board;
        private readonly IPlayerInput  _input;

        private PlayerAction? _nextAction;
        private float         _elapsedTime;
        private bool          _isPlaying;

        // ── Constructor ──────────────────────────────────────────────────────

        /// <summary>Creates a game with specified board and input.</summary>
        public Game(Board board, IPlayerInput input)
        {
            _board = board;
            _input = input;
            PieceFinishedFallingEvent += input.Cancel;
        }

        // ── API pública ──────────────────────────────────────────────────────

        /// <summary>Starts the game.</summary>
        public void Start()
        {
            _isPlaying   = true;
            _elapsedTime = 0;
            Score        = new Score();
            Level        = new Level();
            _board.RemoveAllBlocks();
            AddPiece();
        }

        /// <summary>Resumes paused game.</summary>
        public void Resume() => _isPlaying = true;

        /// <summary>Pauses started game.</summary>
        public void Pause() => _isPlaying = false;

        /// <summary>Sets the player action that the game should process in the next update.</summary>
        public void SetNextAction(PlayerAction action) => _nextAction = action;

        /// <summary>Updates the game by processing user input.</summary>
        public void Update(float deltaTime)
        {
            if (!_isPlaying) return;

            _input.Update();

            var action = _input?.GetPlayerAction();
            if (action.HasValue)
            {
                HandlePlayerAction(action.Value);
            }
            else if (_nextAction.HasValue)
            {
                HandlePlayerAction(_nextAction.Value);
                _nextAction = null;
            }
            else
            {
                HandleAutomaticPieceFalling(deltaTime);
            }
        }

        // ── Lógica interna ───────────────────────────────────────────────────

        private void AddPiece()
        {
            _board.AddPiece();
            if (!_board.HasCollisions()) return;

            _isPlaying = false;
            FinishedEvent();
        }

        private void HandleAutomaticPieceFalling(float deltaTime)
        {
            _elapsedTime += deltaTime;
            if (!(_elapsedTime >= Level.FallDelay)) return;

            if (!_board.MovePieceDown())
                PieceFinishedFalling();

            ResetElapsedTime();
        }

        private void HandlePlayerAction(PlayerAction action)
        {
            var pieceMoved = false;

            switch (action)
            {
                case PlayerAction.MoveLeft:
                    pieceMoved = _board.MovePieceLeft();
                    // NUEVO: notifica movimiento inválido si la pieza no se movió
                    if (!pieceMoved) InvalidMoveEvent();
                    break;

                case PlayerAction.MoveRight:
                    pieceMoved = _board.MovePieceRight();
                    // NUEVO: notifica movimiento inválido si la pieza no se movió
                    if (!pieceMoved) InvalidMoveEvent();
                    break;

                case PlayerAction.MoveDown:
                    ResetElapsedTime();
                    if (_board.MovePieceDown())
                    {
                        pieceMoved = true;
                        Score.PieceMovedDown();
                    }
                    else
                    {
                        PieceFinishedFalling();
                    }
                    break;

                case PlayerAction.Rotate:
                    var didRotate = _board.RotatePiece();
                    if (didRotate)
                        PieceRotatedEvent();
                    break;

                case PlayerAction.Fall:
                    // NUEVO: evento de hard drop antes de ejecutar la caída
                    HardDropEvent();
                    Score.PieceFinishedFalling(_board.FallPiece());
                    ResetElapsedTime();
                    PieceFinishedFalling();
                    break;
            }

            if (pieceMoved)
                PieceMovedEvent();
        }

        private void PieceFinishedFalling()
        {
            PieceFinishedFallingEvent();

            // Guardamos nivel antes de actualizar para detectar subida
            int prevLevel = Level.Number;

            var rowsCount = _board.RemoveFullRows();
            Score.RowsCleared(rowsCount);
            Level.RowsCleared(rowsCount);

            // NUEVO: evento de líneas eliminadas con conteo
            if (rowsCount > 0)
                LinesClearedEvent(rowsCount);

            // NUEVO: evento de subida de nivel
            if (Level.Number > prevLevel)
                LevelUpEvent();

            AddPiece();
        }

        private void ResetElapsedTime() => _elapsedTime = 0;
    }
}