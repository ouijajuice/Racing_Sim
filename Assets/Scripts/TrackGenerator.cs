using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[RequireComponent(typeof(SplineContainer), typeof(MeshFilter), typeof(MeshRenderer))]
public class TrackGenerator : MonoBehaviour
{
    [Header("Track Shape")]
    public int controlPoints = 12;
    public float minRadius = 25f;
    public float maxRadius = 55f;

    [Header("Road Mesh")]
    public int meshResolution = 150;
    public float trackWidth = 8f;

    public List<Vector3> Waypoints { get; private set; } = new();

    private SplineContainer splineContainer;
    private MeshFilter meshFilter;

    void Awake()
    {
        splineContainer = GetComponent<SplineContainer>();
        meshFilter = GetComponent<MeshFilter>();
    }

    public void GenerateTrack()
    {
        Spline spline = splineContainer.Spline;
        spline.Clear();
        spline.Closed = true;

        // 1. Place randomized knots in a circle (prevents self-intersection)
        for (int i = 0; i < controlPoints; i++)
        {
            float angle = (i / (float)controlPoints) * Mathf.PI * 2f;
            float radius = UnityEngine.Random.Range(minRadius, maxRadius);
            float3 pos = new float3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            spline.Add(new BezierKnot(pos), TangentMode.AutoSmooth);
        }

        BuildRoadMeshAndWaypoints(spline);
    }

    private void BuildRoadMeshAndWaypoints(Spline spline)
    {
        Waypoints.Clear();
        Vector3[] vertices = new Vector3[meshResolution * 2];
        int[] triangles = new int[meshResolution * 6];

        for (int i = 0; i < meshResolution; i++)
        {
            float t = i / (float)meshResolution;
            Vector3 center = spline.EvaluatePosition(t);
            Vector3 tangent = ((Vector3)spline.EvaluateTangent(t)).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;

            Waypoints.Add(center);

            // Left and right road edges
            vertices[i * 2] = center - right * (trackWidth * 0.5f);
            vertices[i * 2 + 1] = center + right * (trackWidth * 0.5f);

            // Connect current slice to next slice (wrapping at the end)
            int next = (i + 1) % meshResolution;
            int triIdx = i * 6;

            triangles[triIdx] = i * 2;
            triangles[triIdx + 1] = next * 2;
            triangles[triIdx + 2] = i * 2 + 1;

            triangles[triIdx + 3] = i * 2 + 1;
            triangles[triIdx + 4] = next * 2;
            triangles[triIdx + 5] = next * 2 + 1;
        }

        Mesh mesh = new Mesh { name = "ProceduralTrack" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        meshFilter.mesh = mesh;
    }
}