using UnityEngine;

public static class FeaturesExtension
{
    public static Vector2 XZ(this Vector3 vector) {
        return new Vector2(vector.x, vector.z);
    }
    public static Vector3 X0Z(this Vector3 vector)
    {
        return new Vector3(vector.x, 0, vector.z);
    }
    public static void SetPositionY(this Transform transform, float value)
    {
        var pos = transform.position;
        transform.position = new Vector3(pos.x, value, pos.z);
    }
    public static void AddY(this Transform transform, float value)
    {
        var pos = transform.position;
        transform.position = new Vector3(pos.x, pos.y + value, pos.z);
    }
    /// <summary>
    /// Get the path from current node to root.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <returns></returns>
    public static string GetPath(this GameObject gameObject)
    {
        string path = "/" + gameObject.name;
        Transform transform = gameObject.transform;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = "/" + transform.name + path;
        }
        return path;
    }
    public static Color GetDelayColor(double delay)
    {
        string color = "#00BE00";
        if (delay < 100)
        {
            color = "#00BE00"; // Low delay - Green        
        }
        else if (delay < 300)
        {
            color = "#FFFF00"; // Medium delay - Yellow
        }
        else if (delay < 500)
        {
            color = "#FFA500"; // High delay - Orange            
        }
        else if (delay < 1000)
        {
            color = "#FF0000"; // Very high delay - Red            
        }
        else
        {
            color = "#800080"; // Extremely high delay - Purple   
        }
        ColorUtility.TryParseHtmlString(color, out Color _color);
        return _color;
    }
}
