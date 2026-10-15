using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace RhythmGameConsole
{
    // The entry point initializes the high-precision clock and lists when notes fall
    class Program
    {
        static void Main(string[] args)
        {
            Console.CursorVisible = false;
            Console.Clear();

            // Set up a steady 120 BPM tempo track
            Conductor conductor = new Conductor(bpm: 120f);
            
            // Map out exactly on which beat thresholds an active note should cross the target line
            List<float> noteTargetBeats = new List<float> { 2.0f, 4.0f, 6.0f, 8.0f, 10.0f, 12.0f, 14.0f, 16.0f, 18.0f, 20.0f };
            
            GameEngine game = new GameEngine(conductor, noteTargetBeats);
            game.Start();
        }
    }

    // Handles the core structural synchronization between running CPU clock ticks and musical beats
    public class Conductor
    {
        public float Bpm { get; private set; }
        public float SecondsPerBeat { get; private set; }
        public float SongPositionSeconds { get; private set; }
        public float CurrentBeat { get; private set; }

        private Stopwatch _songTimer;

        public Conductor(float bpm)
        {
            Bpm = bpm;
            SecondsPerBeat = 60f / bpm;
            _songTimer = new Stopwatch();
        }

        public void Start()
        {
            _songTimer.Start();
        }

        public void Update()
        {
            // Pull high-precision timeline information from the running stopwatch hardware
            SongPositionSeconds = (float)_songTimer.Elapsed.TotalSeconds;
            CurrentBeat = SongPositionSeconds / SecondsPerBeat;
        }
    }

    // Drives the active game loop, evaluates millisecond timing inputs, and updates score variables
    public class GameEngine
    {
        private readonly Conductor _conductor;
        private readonly List<float> _noteBeats;
        private readonly HashSet<int> _processedNotes = new HashSet<int>();
        
        // Exact allowable accuracy ranges measured outward in total seconds
        private const float PerfectWindow = 0.05f; 
        private const float GoodWindow = 0.12f;
        private const float MissWindow = 0.20f;

        private string _lastScoreFeedback = "START";
        private int _score = 0;
        private int _combo = 0;
        private int _maxCombo = 0;
        private int _comboMultiplier = 1;
        private int _health = 100; // Starts at max health

        public GameEngine(Conductor conductor, List<float> noteBeats)
        {
            _conductor = conductor;
            _noteBeats = noteBeats;
        }

        public void Start()
        {
            _conductor.Start();

            // Loop continues running until the final note crosses beyond the critical miss threshold OR health hits zero
            while (_conductor.CurrentBeat < _noteBeats[_noteBeats.Count - 1] + 2f && _health > 0)
            {
                _conductor.Update();
                CheckForMissedNotes();
                HandleInput();
                RenderTrack();
                Thread.Sleep(16); // Lock ticks down near a comfortable 60 FPS pacing
            }

            Console.Clear();
            if (_health <= 0)
            {
                Console.WriteLine("=== TRACK FAILED ===");
                Console.WriteLine("Your health dropped to 0%!");
            }
            else
            {
                Console.WriteLine("=== STAGE CLEAR ===");
            }
            Console.WriteLine($"Final Score: {_score}");
            Console.WriteLine($"Max Combo Attained: {_maxCombo}");
        }

        private void HandleInput()
        {
            if (Console.KeyAvailable)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Spacebar)
                {
                    EvaluateHit();
                }
            }
        }

        private void EvaluateHit()
        {
            float currentSeconds = _conductor.SongPositionSeconds;

            for (int i = 0; i < _noteBeats.Count; i++)
            {
                if (_processedNotes.Contains(i)) continue;

                // Calculate the theoretical exact target moment down to the millisecond
                float targetSeconds = _noteBeats[i] * _conductor.SecondsPerBeat;
                float timeDifference = Math.Abs(currentSeconds - targetSeconds);

                // Check if user hit inside the maximum allowable window frame
                if (timeDifference <= MissWindow)
                {
                    _processedNotes.Add(i);
                    int baseScore = 0;

                    if (timeDifference <= PerfectWindow)
                    {
                        _lastScoreFeedback = "PERFECT!!!";
                        baseScore = 300;
                        _combo++;
                        _health = Math.Min(100, _health + 5); // Regenerate health
                    }
                    else if (timeDifference <= GoodWindow)
                    {
                        _lastScoreFeedback = "GOOD!";
                        baseScore = 100;
                        _combo++;
                        _health = Math.Min(100, _health + 2); // Minor health recovery
                    }
                    else
                    {
                        _lastScoreFeedback = "BAD";
                        baseScore = 50;
                        _combo = 0; // Combo breaks on BAD hits
                        _health = Math.Max(0, _health - 10); // Heavy health penalty
                    }

                    // Track maximum combo streak achieved
                    if (_combo > _maxCombo) _maxCombo = _combo;

                    // Calculate score multiplier based on current combo milestones
                    UpdateComboMultiplier();

                    // Dynamic Health Scaler Factor:
                    // Scale factor increases from 1.0x (at 0% health) to 2.0x (at 100% health)
                    // High health directly translates to higher scores!
                    float healthScaleFactor = 1.0f + (_health / 100f); 

                    // Apply both the combo multiplier and dynamic health scale factor to final points
                    _score += (int)(baseScore * _comboMultiplier * healthScaleFactor);
                    return;
                }
            }
        }

        private void CheckForMissedNotes()
        {
            float currentSeconds = _conductor.SongPositionSeconds;

            for (int i = 0; i < _noteBeats.Count; i++)
            {
                if (_processedNotes.Contains(i)) continue;

                float targetSeconds = _noteBeats[i] * _conductor.SecondsPerBeat;
                
                // Clear out unhit notes that slipped past the window completely
                if (currentSeconds - targetSeconds > MissWindow)
                {
                    _processedNotes.Add(i);
                    _lastScoreFeedback = "MISS";
                    _combo = 0;
                    UpdateComboMultiplier();
                    _health = Math.Max(0, _health - 15); // Severe penalty for ignoring a note
                }
            }
        }

        private void UpdateComboMultiplier()
        {
            // Milestone combo tier thresholds (Max 4x)
            if (_combo >= 30)       _comboMultiplier = 4;
            else if (_combo >= 20)  _comboMultiplier = 3;
            else if (_combo >= 10)  _comboMultiplier = 2;
            else                    _comboMultiplier = 1;
        }

        private void RenderTrack()
        {
            int trackLength = 30;
            char[] visualTrack = new char[trackLength];
            for (int i = 0; i < trackLength; i++) visualTrack[i] = '-';

            // Establish the structural hit registration visual marker
            int targetLineIndex = 5;
            visualTrack[targetLineIndex] = '|';

            // Compute exact active pixel placement along the single character line array
            for (int i = 0; i < _noteBeats.Count; i++)
            {
                if (_processedNotes.Contains(i)) continue;

                float beatDifference = _noteBeats[i] - _conductor.CurrentBeat;
                int notePosition = targetLineIndex + (int)(beatDifference * 8f);

                if (notePosition >= targetLineIndex && notePosition < trackLength)
                {
                    visualTrack[notePosition] = 'O';
                }
            }

            // Construct a visual health bar string component
            int healthBarSegments = _health / 10;
            string visualHealthBar = new string('█', healthBarSegments).PadRight(10, '-');
            float currentHealthMultiplier = 1.0f + (_health / 100f);

            // Reposition cursor back up to avoid heavy UI tearing and screen flickering
            Console.SetCursorPosition(0, 0);
            Console.WriteLine("==================================================");
            Console.WriteLine(" Press [SPACEBAR] right when 'O' hits '|'");
            Console.WriteLine("==================================================");
            Console.WriteLine(new string(visualTrack));
            Console.WriteLine("==================================================");
            Console.WriteLine($"Score: {_score.ToString().PadRight(10)} Combo Mult: {_comboMultiplier}x");
            Console.WriteLine($"Combo: {_combo.ToString().PadRight(10)} Rating: {_lastScoreFeedback.PadRight(12)}");
            Console.WriteLine($"Health: [{visualHealthBar}] {_health}%  (Score Factor: {currentHealthMultiplier:F2}x)");
            Console.WriteLine("==================================================");
            Console.WriteLine($"Current Beat: {_conductor.CurrentBeat:F2}");
        }
    }
}
