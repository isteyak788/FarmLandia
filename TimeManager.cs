using UnityEngine;
using System; // Required for System.Action event delegate
using System.Collections.Generic;
using UnityEngine.UI; // Required for Button

public class TimeManager : MonoBehaviour
{
    // Singleton pattern for easy access from other scripts
    public static TimeManager Instance { get; private set; }

    [Header("Game Time Settings")]
    [Tooltip("How many real-world seconds equal one in-game day.")]
    [Range(1f, 3600f)] // 1 second to 1 hour for a game day
    public float realSecondsPerGameDay = 60f; // Default: 1 real minute = 1 game day

    [Tooltip("How many in-game days are in each month.")]
    [Range(1, 31)]
    public int daysPerMonth = 30;

    [Tooltip("How many in-game months are in each year.")]
    [Range(1, 12)]
    public int monthsPerYear = 12;

    [Tooltip("Reference to the SeasonSystem ScriptableObject containing all defined seasons.")]
    public SeasonSystem seasonSystemData; // Reference to the SeasonSystem SO

    [Header("Current Game Time State")]
    [Tooltip("The current game speed multiplier. 1.0 is normal speed.")]
    [Range(0.1f, 10.0f)]
    public float gameSpeedMultiplier = 1.0f; // Default game speed

    // Removed [ReadOnly] attribute to make these fields editable in the Inspector
    [SerializeField] private int _currentDay = 1; // 1-based day of the month
    [SerializeField] private int _currentMonth = 1; // 1-based month of the year
    [SerializeField] private int _currentYear = 1; // 1-based year

    [SerializeField] private int _currentSeasonIndex = 0; // Index into the seasons list
    [SerializeField] private float _currentSeasonProgressDays = 0f; // Progress within the current season

    private float _currentDayProgressRealSeconds = 0f; // Accumulates real seconds to track day progression

    // Events for other systems to subscribe to
    public event Action<int, int, int> OnNewDay; // currentDay, currentMonth, currentYear
    public event Action<Season> OnNewSeason; // currentSeason

    [Header("UI Control")]
    [Tooltip("Assign the UI Button for increasing game speed.")]
    public Button increaseSpeedButton;
    [Tooltip("Assign the UI Button for decreasing game speed.")]
    public Button decreaseSpeedButton;


    void Awake()
    {
        // Implement the Singleton pattern for easy access from other scripts
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // Destroy duplicate instances
            return;
        }
        Instance = this; // Set this as the singleton instance
        DontDestroyOnLoad(gameObject); // Keep TimeManager alive across scene loads

        // Validate if SeasonSystemData is assigned and has seasons
        if (seasonSystemData == null)
        {
            Debug.LogError("TimeManager: SeasonSystemData is not assigned! Please assign it in the inspector.");
            enabled = false; // Disable script if a critical reference is missing
        }
        else if (seasonSystemData.seasons == null || seasonSystemData.seasons.Count == 0) // Corrected from allSeasons to seasons
        {
            Debug.LogWarning("TimeManager: SeasonSystemData has no seasons defined. Creating a default 'Default Season'.");
            // Create a default season if none are defined, to prevent null reference errors
            Season defaultSeason = ScriptableObject.CreateInstance<Season>();
            defaultSeason.seasonName = "Default Season";
            seasonSystemData.seasons.Add(defaultSeason); // Corrected from allSeasons to seasons
        }
    }

    void Start()
    {
        // Set Unity's Time.timeScale initially to match our gameSpeedMultiplier
        Time.timeScale = gameSpeedMultiplier;

        // Trigger initial events for other systems to synchronize their state
        OnNewDay?.Invoke(_currentDay, _currentMonth, _currentYear);
        OnNewSeason?.Invoke(GetCurrentSeason());

        // Assign button listeners if buttons are provided
        if (increaseSpeedButton != null)
        {
            increaseSpeedButton.onClick.AddListener(IncreaseGameSpeed);
        }
        if (decreaseSpeedButton != null)
        {
            decreaseSpeedButton.onClick.AddListener(DecreaseGameSpeed);
        }
    }

    void OnDisable()
    {
        // Remove button listeners to prevent memory leaks when the object is disabled or destroyed
        if (increaseSpeedButton != null)
        {
            increaseSpeedButton.onClick.RemoveListener(IncreaseGameSpeed);
        }
        if (decreaseSpeedButton != null)
        {
            decreaseSpeedButton.onClick.RemoveListener(DecreaseGameSpeed);
        }
    }

    void Update()
    {
        // Advance real-world time based on game speed
        _currentDayProgressRealSeconds += Time.deltaTime * gameSpeedMultiplier;

        // Loop to advance game days if enough real-world time has passed
        while (_currentDayProgressRealSeconds >= realSecondsPerGameDay)
        {
            _currentDayProgressRealSeconds -= realSecondsPerGameDay; // Subtract a full game day's worth of real seconds
            AdvanceGameDay(); // Advance the game day
        }
    }

    /// <summary>
    /// Advances the game by one day, handling month, year, and season rollovers.
    /// </summary>
    private void AdvanceGameDay()
    {
        _currentDay++;
        // Season progress is now updated by TimeManager
        _currentSeasonProgressDays++;

        // Check for month rollover
        if (_currentDay > daysPerMonth)
        {
            _currentDay = 1; // Reset day to 1
            _currentMonth++; // Advance to next month

            // Check for year rollover
            if (_currentMonth > monthsPerYear)
            {
                _currentMonth = 1; // Reset month to 1
                _currentYear++;    // Advance to next year
            }
        }

        // Check for season rollover (if seasons are defined)
        if (seasonSystemData != null && seasonSystemData.seasons != null && seasonSystemData.seasons.Count > 0) // Corrected from allSeasons to seasons
        {
            // Use daysPerSeason from the SeasonSystemData
            if (_currentSeasonProgressDays >= seasonSystemData.daysPerSeason)
            {
                // Carry over any excess progress to the new season
                _currentSeasonProgressDays -= seasonSystemData.daysPerSeason;
                // Cycle to the next season
                _currentSeasonIndex = (_currentSeasonIndex + 1) % seasonSystemData.seasons.Count;
                Debug.Log($"New Season: {GetCurrentSeason().seasonName} (Day {_currentDay}, Month {_currentMonth}, Year {_currentYear})");
                OnNewSeason?.Invoke(GetCurrentSeason()); // Notify subscribers of the new season
            }
        }

        // Notify subscribers that a new game day has started
        OnNewDay?.Invoke(_currentDay, _currentMonth, _currentYear);
        // Debug.Log($"New Day: Day {_currentDay}, Month {_currentMonth}, Year {_currentYear}"); // Optional debug log
    }

    /// <summary>
    /// Increases the game speed multiplier.
    /// </summary>
    public void IncreaseGameSpeed()
    {
        // Increase by 1, capped at 10x
        gameSpeedMultiplier = Mathf.Min(gameSpeedMultiplier + 1.0f, 10.0f);
        Time.timeScale = gameSpeedMultiplier; // Update Unity's Time.timeScale
        Debug.Log($"Game Speed increased to: {gameSpeedMultiplier}x (Unity Time.timeScale: {Time.timeScale})");
    }

    /// <summary>
    /// Decreases the game speed multiplier.
    /// </summary>
    public void DecreaseGameSpeed()
    {
        // Decrease by 1, min speed at 0.1x (to avoid pausing entirely unless explicitly intended)
        gameSpeedMultiplier = Mathf.Max(gameSpeedMultiplier - 1.0f, 0.1f);
        Time.timeScale = gameSpeedMultiplier; // Update Unity's Time.timeScale
        Debug.Log($"Game Speed decreased to: {gameSpeedMultiplier}x (Unity Time.timeScale: {Time.timeScale})");
    }

    // Public getters for other scripts to access current game time information
    public int CurrentDay => _currentDay;
    public int CurrentMonth => _currentMonth;
    public int CurrentYear => _currentYear;

    /// <summary>
    /// Gets the current active Season ScriptableObject.
    /// </summary>
    public Season GetCurrentSeason()
    {
        if (seasonSystemData == null || seasonSystemData.seasons == null || seasonSystemData.seasons.Count == 0) return null; // Corrected from allSeasons to seasons
        return seasonSystemData.seasons[_currentSeasonIndex]; // Corrected from allSeasons to seasons
    }

    /// <summary>
    /// Gets the normalized progress (0.0 to 1.0) within the current season.
    /// </summary>
    public float GetCurrentSeasonProgressNormalized()
    {
        return _currentSeasonProgressDays / seasonSystemData.daysPerSeason;
    }

    /// <summary>
    /// Converts a real-time delta into an equivalent amount of game days passed.
    /// </summary>
    /// <param name="realDeltaTime">The real-world time in seconds.</param>
    /// <returns>The equivalent amount of game days that have passed.</returns>
    public float GetGameDaysPassed(float realDeltaTime)
    {
        return (realDeltaTime * gameSpeedMultiplier) / realSecondsPerGameDay;
    }
}

// NOTE: The ReadOnlyAttribute and ReadOnlyDrawer are removed from this script.
// If you want to use them again for other fields, you will need to put them back
// in a separate ReadOnlyDrawer.cs file inside an Assets/Editor/ folder.
