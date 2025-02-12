using UnityEngine;

public class Painter : MonoBehaviour
{
    public Texture2D maskTexture;
    public Color paintColor = Color.red;
    private Texture2D paintTexture;
    
    void Start()
    {
        if (maskTexture != null)
        {
            paintTexture = new Texture2D(maskTexture.width, maskTexture.height);
            paintTexture.SetPixels(maskTexture.GetPixels());
            paintTexture.Apply();
        }
    }
    
    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Paint(mousePosition);
        }
    }
    
    void Paint(Vector2 position)
    {
        int x = (int)(position.x * paintTexture.width);
        int y = (int)(position.y * paintTexture.height);
        
        if (x >= 0 && x < paintTexture.width && y >= 0 && y < paintTexture.height)
        {
            paintTexture.SetPixel(x, y, paintColor);
            paintTexture.Apply();
        }
    }
}