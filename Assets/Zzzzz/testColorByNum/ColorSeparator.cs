using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class ColorSeparator : MonoBehaviour
{
    public Texture2D sourceImage;
    public float rgbTolerance = 0.1f;
    public float blackTolerance = 0.1f;
    public Color maskColor = Color.white;
    public Button calculateButton;
    public Button clearButton;
    
    private Dictionary<Color, Texture2D> colorMasks = new Dictionary<Color, Texture2D>();

    void Start()
    {
        calculateButton.onClick.AddListener(GenerateColorMasks);
        clearButton.onClick.AddListener(ClearMasks);
    }

    void GenerateColorMasks()
    {
        int width = sourceImage.width;
        int height = sourceImage.height;
        Color[] pixels = sourceImage.GetPixels();

        Dictionary<Color, List<Vector2Int>> colorPositions = new Dictionary<Color, List<Vector2Int>>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Color pixelColor = sourceImage.GetPixel(x, y);
                if (pixelColor.a > 0.1f && !IsBlack(pixelColor))
                {
                    if (!colorPositions.ContainsKey(pixelColor))
                    {
                        colorPositions[pixelColor] = new List<Vector2Int>();
                    }
                    colorPositions[pixelColor].Add(new Vector2Int(x, y));
                }
            }
        }

        foreach (var entry in colorPositions)
        {
            Texture2D mask = new Texture2D(width, height);
            mask.SetPixels(new Color[width * height]);

            foreach (Vector2Int pos in entry.Value)
            {
                mask.SetPixel(pos.x, pos.y, maskColor);
            }

            mask.Apply();
            colorMasks[entry.Key] = mask;
        }
    }

    bool IsBlack(Color color)
    {
        return color.r < blackTolerance && color.g < blackTolerance && color.b < blackTolerance;
    }

    public void ClearMasks()
    {
        colorMasks.Clear();
    }

    public Dictionary<Color, Texture2D> GetColorMasks()
    {
        return colorMasks;
    }
}