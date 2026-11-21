using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class RaceManager : MonoBehaviour
{
    [Header("For External Access")]
    public IReadOnlyList<AICar> Cars => spawnedCars;

    public TrackGenerator trackGenerator;
    public AICar carPrefab;
    public int numberOfCars = 8;
    public int totalLaps = 3;

    [Header("Leaderboard Refreshing")]
    public float leaderboardRefreshRate = 0.2f;
    private float leaderboardTimer = 0f;

    [Header("End of Race")]
    private float raceTimer = 0f;
    private int finishedCount = 0;

    [Header("UI")]
    public TMP_Text leaderboardText;

    private List<AICar> spawnedCars = new();
    private StringBuilder sb = new();

    void Start()
    {
        StartNewRace();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            StartNewRace();
        }

        raceTimer += Time.deltaTime;

        // Lock each car's position the exact frame it finishes
        foreach (var car in spawnedCars)
        {
            if (!car.HasFinished && car.CurrentLap > totalLaps)
            {
                finishedCount++;
                car.LockFinish(finishedCount, raceTimer);
            }
        }

        leaderboardTimer += Time.deltaTime;
        if (leaderboardTimer >= leaderboardRefreshRate)
        {
            leaderboardTimer = 0f;
            UpdateLeaderboard();
        }
    }

    public void StartNewRace()
    {
        raceTimer = 0f;
        finishedCount = 0;

        foreach (var car in spawnedCars) Destroy(car.gameObject);
        spawnedCars.Clear();

        trackGenerator.GenerateTrack();
        var waypoints = trackGenerator.Waypoints;

        for (int i = 0; i < numberOfCars; i++)
        {
            int wpIndex = i * 2;
            Vector3 spawnPos = waypoints[wpIndex] + Vector3.up * 0.5f;
            Vector3 lookDir = (waypoints[(wpIndex + 1) % waypoints.Count] - waypoints[wpIndex]).normalized;

            AICar car = Instantiate(carPrefab, spawnPos, Quaternion.LookRotation(lookDir));

            float randomSpeed = Random.Range(18f, 25f);
            float randomLane = Random.Range(-trackGenerator.trackWidth * 0.3f, trackGenerator.trackWidth * 0.3f);
            Color randomColor = Color.HSVToRGB(i / (float)numberOfCars, 0.85f, 0.9f);

            car.Initialize(waypoints, wpIndex + 1, randomSpeed, randomLane, randomColor, $"Car {i + 1}");
            spawnedCars.Add(car);
        }
    }

    private void UpdateLeaderboard()
    {
        if (leaderboardText == null || spawnedCars.Count == 0) return;

        // Finished cars always stay at the top in their locked order; racing cars sort by progress
        var sortedCars = spawnedCars
            .OrderBy(c => c.HasFinished ? 0 : 1)
            .ThenBy(c => c.HasFinished ? c.FinishPosition : int.MaxValue)
            .ThenByDescending(c => c.RaceProgress)
            .ToList();

        bool raceOver = finishedCount == spawnedCars.Count;
        int leaderLap = Mathf.Min(sortedCars[0].CurrentLap, totalLaps);

        sb.Clear();
        sb.AppendLine(raceOver ? "<b>FINAL RESULTS</b>" : $"<b>LAP {leaderLap} / {totalLaps}</b>");
        sb.AppendLine("----------------");

        float winnerTime = sortedCars[0].HasFinished ? sortedCars[0].FinishTime : 0f;

        for (int i = 0; i < sortedCars.Count; i++)
        {
            AICar car = sortedCars[i];
            string hex = ColorUtility.ToHtmlStringRGB(car.CarColor);
            string status;

            if (car.HasFinished)
            {
                // Show total time for 1st place, and time gap (+0.85s) for everyone else
                status = i == 0
                    ? FormatTime(car.FinishTime)
                    : $"+{(car.FinishTime - winnerTime):0.00}s";
            }
            else
            {
                status = $"L{car.CurrentLap}";
            }

            sb.AppendLine($"{i + 1}. <color=#{hex}>●</color> {car.CarName}  <color=#AAAAAA>{status}</color>");
        }

        leaderboardText.text = sb.ToString();
    }

    private string FormatTime(float seconds)
    {
        int mins = Mathf.FloorToInt(seconds / 60f);
        float secs = seconds % 60f;
        return $"{mins:00}:{secs:00.00}";
    }
}