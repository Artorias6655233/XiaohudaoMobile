using UnityEngine;

public static class Coordinate
{
    // 定义一些常量
    private const float x_PI = 3.14159265358979324f * 3000.0f / 180.0f;
    private const float PI = 3.1415926535897932384626f;
    private const float a = 6378245.0f;
    private const float ee = 0.00669342162296594323f;

    public static float[] BD09ToGCJ02(float[] bdLngLat)
    {
        float bdLng = bdLngLat[0];
        float bdLat = bdLngLat[1];
        float x = bdLng - 0.0065f;
        float y = bdLat - 0.006f;
        float z = Mathf.Sqrt(x * x + y * y) - 0.00002f * Mathf.Sin(y * x_PI);
        float theta = Mathf.Atan2(y, x) - 0.000003f * Mathf.Cos(x * x_PI);
        float ggLng = z * Mathf.Cos(theta);
        float ggLat = z * Mathf.Sin(theta);
        return new float[] { ggLng, ggLat };
    }

    public static float[] GCJ02ToBD09(float[] lnglat)
    {
        float lng = lnglat[0];
        float lat = lnglat[1];
        float z = Mathf.Sqrt(lng * lng + lat * lat) + 0.00002f * Mathf.Sin(lat * x_PI);
        float theta = Mathf.Atan2(lat, lng) + 0.000003f * Mathf.Cos(lng * x_PI);
        float bdLng = z * Mathf.Cos(theta) + 0.0065f;
        float bdLat = z * Mathf.Sin(theta) + 0.006f;
        return new float[] { bdLng, bdLat };
    }

    public static float[] WGS84ToGCJ02(float[] lnglat)
    {
        float lng = lnglat[0];
        float lat = lnglat[1];
        if (OutOfChina(lng, lat))
        {
            return new float[] { lng, lat };
        }
        else
        {
            float dLat = TransformLat(lng - 105.0f, lat - 35.0f);
            float dLng = TransformLng(lng - 105.0f, lat - 35.0f);
            float radLat = lat / 180.0f * PI;
            float magic = Mathf.Sin(radLat);
            magic = 1 - ee * magic * magic;
            float sqrtMagic = Mathf.Sqrt(magic);
            dLat = (dLat * 180.0f) / (a * (1 - ee) / (magic * sqrtMagic) * PI);
            dLng = (dLng * 180.0f) / (a / sqrtMagic * Mathf.Cos(radLat) * PI);
            float mglat = lat + dLat;
            float mglng = lng + dLng;
            return new float[] { mglng, mglat };
        }
    }

    public static float[] GCJ02ToWGS84(float[] lnglat)
    {
        float lng = lnglat[0];
        float lat = lnglat[1];
        if (OutOfChina(lng, lat))
        {
            return new float[] { lng, lat };
        }
        else
        {
            float dLat = TransformLat(lng - 105.0f, lat - 35.0f);
            float dLng = TransformLng(lng - 105.0f, lat - 35.0f);
            float radLat = lat / 180.0f * PI;
            float magic = Mathf.Sin(radLat);
            magic = 1 - ee * magic * magic;
            float sqrtMagic = Mathf.Sqrt(magic);
            dLat = (dLat * 180.0f) / (a * (1 - ee) / (magic * sqrtMagic) * PI);
            dLng = (dLng * 180.0f) / (a / sqrtMagic * Mathf.Cos(radLat) * PI);
            float mglat = lat + dLat;
            float mglng = lng + dLng;
            return new float[] { lng * 2 - mglng, lat * 2 - mglat };
        }
    }

    private static float TransformLat(float lng, float lat)
    {
        float ret = -100.0f + 2.0f * lng + 3.0f * lat + 0.2f * lat * lat + 0.1f * lng * lat + 0.2f * Mathf.Sqrt(Mathf.Abs(lng));
        ret += (20.0f * Mathf.Sin(6.0f * lng * PI) + 20.0f * Mathf.Sin(2.0f * lng * PI)) * 2.0f / 3.0f;
        ret += (20.0f * Mathf.Sin(lat * PI) + 40.0f * Mathf.Sin(lat / 3.0f * PI)) * 2.0f / 3.0f;
        ret += (160.0f * Mathf.Sin(lat / 12.0f * PI) + 320f * Mathf.Sin(lat * PI / 30.0f)) * 2.0f / 3.0f;
        return ret;
    }

    private static float TransformLng(float lng, float lat)
    {
        float ret = 300.0f + lng + 2.0f * lat + 0.1f * lng * lng + 0.1f * lng * lat + 0.1f * Mathf.Sqrt(Mathf.Abs(lng));
        ret += (20.0f * Mathf.Sin(6.0f * lng * PI) + 20.0f * Mathf.Sin(2.0f * lng * PI)) * 2.0f / 3.0f;
        ret += (20.0f * Mathf.Sin(lng * PI) + 40.0f * Mathf.Sin(lng / 3.0f * PI)) * 2.0f / 3.0f;
        ret += (150.0f * Mathf.Sin(lng / 12.0f * PI) + 300.0f * Mathf.Sin(lng / 30.0f * PI)) * 2.0f / 3.0f;
        return ret;
    }

    private static bool OutOfChina(float lng, float lat)
    {
        // 纬度 3.86~53.55, 经度 73.66~135.05
        return !(lng > 73.66f && lng < 135.05f && lat > 3.86f && lat < 53.55f);
    }
}