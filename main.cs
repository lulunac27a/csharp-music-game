using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace RhythmGameConsole
{
    // The entry point initializes the high-precision clock and lists when notes fall
    class Program
    {
        static void Main(string[] args)//main entry point for the rhythm game
        {
            Console.CursorVisible = false;//hide the cursor for a cleaner visual experience
            Console.Clear();//clear the console to prepare for the game interface

            // Set up a steady 120 BPM tempo track
            Conductor conductor = new Conductor(bpm: 120f);//initialize the conductor with a beats per minute value of 120

            // Map out exactly on which beat thresholds an active note should cross the target line
            List<float> noteTargetBeats = new List<float> { 2.0f, 4.0f, 6.0f, 8.0f, 10.0f, 12.0f, 14.0f, 16.0f, 18.0f, 20.0f };//define the beats at which notes will appear in the game

            GameEngine game = new GameEngine(conductor, noteTargetBeats);//initialize the game engine with the conductor and the list of note beats
            game.Start();//start the game loop, which will run until the player either completes the track or fails by losing all health
        }
    }

    // Handles the core structural synchronization between running CPU clock ticks and musical beats
    public class Conductor
    {
        public float Bpm { get; private set; }//beats per minute, defining the tempo of the track
        public float SecondsPerBeat { get; private set; }//calculated duration of each beat in seconds, derived from the BPM
        public float SongPositionSeconds { get; private set; }//current position in the song in seconds, updated every tick
        public float CurrentBeat { get; private set; }//current beat number, calculated based on the song position and seconds per beat

        private Stopwatch _songTimer;//high-precision stopwatch to track elapsed time since the song started

        public Conductor(float bpm)//constructor for the Conductor class, initializing the BPM and calculating seconds per beat
        {
            Bpm = bpm;
            SecondsPerBeat = 60f / bpm;
            _songTimer = new Stopwatch();
        }

        public void Start()//method to start the stopwatch timer, marking the beginning of the song
        {
            _songTimer.Start();
        }

        public void Update()//method to update the current song position and beat based on the elapsed time from the stopwatch
        {
            // Pull high-precision timeline information from the running stopwatch hardware
            SongPositionSeconds = (float)_songTimer.Elapsed.TotalSeconds;
            CurrentBeat = SongPositionSeconds / SecondsPerBeat;
        }
    }

    // Drives the active game loop, evaluates millisecond timing inputs, and updates score variables
    public class GameEngine//game engine class responsible for managing the game state, processing user input, and rendering the game interface
    {
        private readonly Conductor _conductor;
        private readonly List<float> _noteBeats;
        private readonly HashSet<int> _processedNotes = new HashSet<int>();

        // Exact allowable accuracy ranges measured outward in total seconds
        private const float PerfectWindow = 0.05f;// 50 milliseconds for a perfect hit
        private const float GoodWindow = 0.12f;// 120 milliseconds for a good hit
        private const float MissWindow = 0.20f;// 200 milliseconds for a miss threshold

        private string _lastScoreFeedback = "START";
        private int _score = 0;//initial score starts at zero
        private int _combo = 0;//initial combo count starts at zero
        private int _maxCombo = 0;//tracks the maximum combo achieved during the game session
        private int _comboMultiplier = 1;//initial combo multiplier starts at 1x, increasing with higher combos
        private int _health = 100; // Starts at max health

        public GameEngine(Conductor conductor, List<float> noteBeats)
        {
            _conductor = conductor;
            _noteBeats = noteBeats;
        }

        public void Start()//main game loop that runs until the player either completes the track or fails by losing all health
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
            if (_health <= 0)//if the player's health has dropped to zero, display a failure message
            {
                Console.WriteLine("=== TRACK FAILED ===");
                Console.WriteLine("Your health dropped to 0%!");
            }
            else//if the player successfully completed the track, display a success message
            {
                Console.WriteLine("=== STAGE CLEAR ===");
            }
            Console.WriteLine($"Final Score: {_score}");
            Console.WriteLine($"Max Combo Attained: {_maxCombo}");
        }

        private void HandleInput()//method to check for user input, specifically looking for the spacebar key press to evaluate hits
        {
            if (Console.KeyAvailable)//check if a key has been pressed in the console
            {
                ConsoleKeyInfo key = Console.ReadKey(true);//read the key press without displaying it in the console
                if (key.Key == ConsoleKey.Spacebar)//if the spacebar is pressed, evaluate the timing of the hit against the expected note beats
                {
                    EvaluateHit();
                }
            }
        }

        private void EvaluateHit()//method to evaluate the timing of the user's hit against the expected note beats, updating score and health accordingly
        {
            float currentSeconds = _conductor.SongPositionSeconds;//get the current position in the song in seconds

            for (int i = 0; i < _noteBeats.Count; i++)//iterate through each note beat to check if the hit was within the allowable timing windows
            {
                if (_processedNotes.Contains(i)) continue;//skip already processed notes to avoid double scoring

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
                    if (_combo > _maxCombo) _maxCombo = _combo;//update the maximum combo if the current combo exceeds it

                    // Calculate score multiplier based on current combo milestones
                    UpdateComboMultiplier();//update the combo multiplier based on the current combo count

                    // Dynamic Health Scaler Factor:
                    // Scale factor increases from 1.0x (at 0% health) to 2.0x (at 100% health)
                    // High health directly translates to higher scores!
                    float healthScaleFactor = 1.0f + (_health / 100f); // Health scale factor ranges from 1.0 to 2.0 based on current health percentage

                    // Apply both the combo multiplier and dynamic health scale factor to final points
                    _score += (int)(baseScore * _comboMultiplier * healthScaleFactor);//calculate the final score by applying the base score, combo multiplier, and health scale factor
                    return;
                }
            }
        }

        private void CheckForMissedNotes()//method to check for notes that have been missed by the player, updating score and health accordingly
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

        private void UpdateComboMultiplier()//method to update the combo multiplier based on the current combo count, with defined thresholds for increasing multipliers
        {
            // Milestone combo tier thresholds (Max 4x)
            if (_combo >= 30) _comboMultiplier = 4;
            else if (_combo >= 20) _comboMultiplier = 3;
            else if (_combo >= 10) _comboMultiplier = 2;
            else _comboMultiplier = 1;
        }

        private void RenderTrack()//method to render the visual representation of the game track, including notes, target line, score, combo, and health bar
        {
            int trackLength = 30;//define the length of the visual track in characters
            char[] visualTrack = new char[trackLength];
            for (int i = 0; i < trackLength; i++) visualTrack[i] = '-';//initialize the visual track with dashes to represent empty space

            // Establish the structural hit registration visual marker
            int targetLineIndex = 5;
            visualTrack[targetLineIndex] = '|';

            // Compute exact active pixel placement along the single character line array
            for (int i = 0; i < _noteBeats.Count; i++)//iterate through each note beat to determine its position on the visual track based on the current beat and the note's beat timing
            {
                if (_processedNotes.Contains(i)) continue;//skip already processed notes to avoid rendering them again

                float beatDifference = _noteBeats[i] - _conductor.CurrentBeat;//calculate the difference between the note's beat and the current beat to determine how far away it is from the target line
                int notePosition = targetLineIndex + (int)(beatDifference * 8f);//calculate the visual position of the note on the track, scaling the beat difference to fit within the track length

                if (notePosition >= targetLineIndex && notePosition < trackLength)//if the calculated note position is within the bounds of the visual track, place an 'O' character to represent the note
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
