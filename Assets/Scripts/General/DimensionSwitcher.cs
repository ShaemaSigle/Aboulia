using System;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class DimensionSwitcher : MonoBehaviour {
    public Transform player;
    public LevelManager levelManager;
    private Plane _slicingPlane; // The plane that is used for slicing 3D objects when switching from 3D to 2D
    public GameObject[] slicableObjects = new GameObject[1]; //All the objects that will be sliced after dimension switch, can be modified in th editor
    // Store the intersection points
    private List<Vector3> _intersectionPoints = new List<Vector3>();
    private String tagOfSlicedObject;
    public Sprite mySprite;

    void Update() {
        // Trigger dimension switch
        if (Input.GetKeyDown(KeyCode.T)){
            Vector3 forwardDirection = player.forward;
            _slicingPlane = new Plane(forwardDirection, player.position);
            Debug.DrawLine(player.position, player.position + forwardDirection, Color.green);
            DrawSlicingPlane(_slicingPlane, player.position);
            // Slice the object and generate 2D geometry
            foreach (GameObject objectToSlice in slicableObjects){
                SliceObject(objectToSlice);
                Generate2DPolygonFromIntersections(_intersectionPoints);
            }
            levelManager.SwitchTo2D();
        }
    }
    
    // For debug purposes
    void DrawSlicingPlane(Plane slicingPlane, Vector3 planeCenter, float planeSize = 5.0f){
        // Get the normal of the plane
        Vector3 planeNormal = slicingPlane.normal;
        // Draw the normal direction
        Debug.DrawRay(planeCenter, planeNormal * 2.0f, Color.red); // Red line for the normal
        // Find two vectors that are perpendicular to the plane's normal
        Vector3 planeRight = Vector3.Cross(planeNormal, Vector3.up).normalized;
        if (planeRight == Vector3.zero)
            // If planeNormal is pointing straight up or down, choose another axis to cross with
            planeRight = Vector3.Cross(planeNormal, Vector3.forward).normalized;
        
        Vector3 planeUp = Vector3.Cross(planeNormal, planeRight).normalized;
        // Calculate the four corners of a square on the plane
        Vector3 corner1 = planeCenter + (planeRight * planeSize / 2) + (planeUp * planeSize / 2);
        Vector3 corner2 = planeCenter + (planeRight * planeSize / 2) - (planeUp * planeSize / 2);
        Vector3 corner3 = planeCenter - (planeRight * planeSize / 2) - (planeUp * planeSize / 2);
        Vector3 corner4 = planeCenter - (planeRight * planeSize / 2) + (planeUp * planeSize / 2);
        // Draw the square representing a portion of the plane
        Debug.DrawLine(corner1, corner2, Color.green); // Green for the plane surface
        Debug.DrawLine(corner2, corner3, Color.green);
        Debug.DrawLine(corner3, corner4, Color.green);
        Debug.DrawLine(corner4, corner1, Color.green);
    }

    void SliceObject(GameObject obj) {
        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
        if (!meshFilter) return;
        
        Mesh mesh = meshFilter.mesh;
        Vector3[] localVertices = mesh.vertices; // These are local-space vertices
        // Get the local-to-world matrix, which includes position, rotation, and scale
        Matrix4x4 localToWorld = obj.transform.localToWorldMatrix;

        // Convert local vertices to world space, including scaling
        Vector3[] worldVertices = new Vector3[localVertices.Length];
        for (int i = 0; i < localVertices.Length; i++) // Transform the local vertex by the object's local-to-world matrix
            worldVertices[i] = localToWorld.MultiplyPoint3x4(localVertices[i]);
        int[] triangles = mesh.triangles;

        // Clear previous intersection points
        _intersectionPoints.Clear();
        // Defining the slicing plane's local coordinate system (planeRight and planeUp)
        // If needed to stop projecting slices, this block can be commented out
        Vector3 planeNormal = _slicingPlane.normal;
        Vector3 planeRight = Vector3.Cross(planeNormal, Vector3.up).normalized;
        if (planeRight == Vector3.zero)
            planeRight = Vector3.Cross(planeNormal, Vector3.forward).normalized;
        Vector3 planeUp = Vector3.Cross(planeNormal, planeRight).normalized;

        // Iterate over all triangles in the mesh
        for (int i = 0; i < triangles.Length; i += 3){
            Vector3 v0 = worldVertices[triangles[i]];
            Vector3 v1 = worldVertices[triangles[i + 1]];
            Vector3 v2 = worldVertices[triangles[i + 2]];
            
            // Determine the side of the slicing plane for each vertex
            bool v0Above = _slicingPlane.GetSide(v0);
            bool v1Above = _slicingPlane.GetSide(v1);
            bool v2Above = _slicingPlane.GetSide(v2);
            
            // Checking if this triangle intersects the slicing plane & finding the intersection points on the triangle's edges
            // If needed to stop projecting slices, (ProjectTo2D(intersection, planeRight, planeUp)) can be switched to FindIntersection(x, x)
            if (v0Above != v1Above)
                _intersectionPoints.Add(ProjectTo2D(FindIntersection(v0, v1), planeRight, planeUp));
            if (v1Above != v2Above)
                _intersectionPoints.Add(ProjectTo2D(FindIntersection(v1, v2), planeRight, planeUp));
            if (v2Above != v0Above)
                _intersectionPoints.Add(ProjectTo2D(FindIntersection(v2, v0), planeRight, planeUp));
        }
        tagOfSlicedObject = obj.tag;
    }

    // Function to find the intersection point between two vertices and the slicing plane
    Vector3 FindIntersection(Vector3 v1, Vector3 v2){
        float distance1 = _slicingPlane.GetDistanceToPoint(v1);
        float distance2 = _slicingPlane.GetDistanceToPoint(v2);
        float t = distance1 / (distance1 - distance2);
        return Vector3.Lerp(v1, v2, t);  // Interpolating to find the intersection point
    }
    
    // It is needed to connect the intersection points in a 2D space to create the polygon, triangulate the polygon for rendering in Unity
    void Generate2DPolygonFromIntersections(List<Vector3> polygon2D) {
        if (polygon2D.Count < 3) return; // We need at least 3 points to create a polygon
        // Create a new GameObject for the 2D polygon
        GameObject polygonObject = new GameObject("Sliced2DPolygon", typeof(PolygonCollider2D), typeof(SpriteRenderer));
        polygonObject.tag = tagOfSlicedObject;
        // Calculating the centroid (center point) of the polygon
        Vector3 centroid = Vector3.zero;
        foreach (var vertex in polygon2D) centroid += vertex;
        centroid /= polygon2D.Count;
        
        // Sort vertices to ensure correct triangulation-
        CleanupVertices(ref polygon2D);
        SortVerticesClockwise(ref polygon2D, centroid);

        // Create a 2D collider using the 2D vertices
        Vector2[] colliderPoints = new Vector2[polygon2D.Count];
        PolygonCollider2D polygonCollider = polygonObject.GetComponent<PolygonCollider2D>();
        // The collider needs to be centered relative to the center of the game object
        Vector3 distanceDiff = centroid - polygonObject.transform.position;
        for (int i = 0; i < polygon2D.Count; i++)
            colliderPoints[i] = new Vector2(polygon2D[i].x - distanceDiff.x, polygon2D[i].y - distanceDiff.y);
        polygonCollider.points = colliderPoints;
        
        // Set up the SpriteRenderer
        SpriteRenderer spriteRenderer = polygonObject.GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = mySprite; // Assign your sprite here

        // Fit the sprite to the collider
        FitSpriteToCollider(spriteRenderer, polygonCollider);
    }

    void CleanupVertices(ref List<Vector3> polygon2D) {
        for (int i = 2; i < polygon2D.Count; i++) {
            // Calculate the area of the triangle formed by the points, if it is zero, the points are collinear
            float area = polygon2D[i-2].x * (polygon2D[i-1].y - polygon2D[i].y) +
                         polygon2D[i-1].x * (polygon2D[i].y - polygon2D[i-2].y) +
                         polygon2D[i].x * (polygon2D[i-2].y - polygon2D[i-1].y);
            if (!Mathf.Approximately(area, 0.0f)) continue; // We don't need to remove one of the point if they are not collinear
            // Calculate distances between each pair of points
            float d1 = Vector3.Distance(polygon2D[i-2], polygon2D[i-1]);
            float d2 = Vector3.Distance(polygon2D[i-1], polygon2D[i]);
            float d3 = Vector3.Distance(polygon2D[i-2], polygon2D[i]);

            // Check which point is in the middle
            if (Mathf.Approximately(d1 + d2, d3))      // polygon2D[i-1] is in the middle
                for (int j = i-1; j < polygon2D.Count - 1; j++) 
                    polygon2D[j] = polygon2D[j + 1];
            else if (Mathf.Approximately(d1 + d3, d2)) // polygon2D[i-2] is in the middle
                for (int j = i-2; j < polygon2D.Count - 1; j++) 
                    polygon2D[j] = polygon2D[j + 1];
            else                                       // polygon2D[i] is in the middle
                for (int j = i; j < polygon2D.Count - 1; j++) 
                    polygon2D[j] = polygon2D[j + 1];
            
            polygon2D.RemoveAt(polygon2D.Count - 1); // Remove the last element since it's now a duplicate after shifting
            i--; // Decrement 'i' to recheck the current position (since it now contains the next element)
        }
        // Getting rid of duplicates 
        polygon2D = polygon2D.Distinct().ToList();
    }
    
    //After slicing vertices can be created randomly, so in order to create triangles correctly the vertices have to be sorted clockwise
    void SortVerticesClockwise(ref List<Vector3> vertices, Vector3 centroid){
        // Sorting vertices based on their angle from the centroid
        vertices.Sort((a, b) => {
            float angleA = Mathf.Atan2(a.y - centroid.y, a.x - centroid.x);
            float angleB = Mathf.Atan2(b.y - centroid.y, b.x - centroid.x);
            return angleA.CompareTo(angleB);
        });
    }
    
    // Function to project a 3D point onto the slicing plane's 2D space
    Vector2 ProjectTo2D(Vector3 point, Vector3 planeRight, Vector3 planeUp) {
        // Convert the point into the plane's local 2D coordinate system
        Vector3 localPoint = point - player.position; // Translate relative to plane origin
        float x = Vector3.Dot(localPoint, planeRight); // X-coordinate
        float y = Vector3.Dot(localPoint, planeUp);    // Y-coordinate
        return new Vector2(x, y);                      // Return as 2D point
    }
    void FitSpriteToCollider(SpriteRenderer spriteRenderer, PolygonCollider2D collider) {
        // Set the SpriteRenderer to use Tiled mode or Sliced mode, which allows resizing without affecting scale
        spriteRenderer.drawMode = SpriteDrawMode.Sliced;
        // Get the bounds of the PolygonCollider2D
        Bounds colliderBounds = collider.bounds;
        // Set the size of the SpriteRenderer to match the collider's size
        Vector2 newSize = new Vector2(colliderBounds.size.x, colliderBounds.size.y);
        spriteRenderer.size = newSize;
    }
}