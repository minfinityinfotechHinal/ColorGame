using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;

#if UNITY_WEBGL
using System.IO;
#endif

public class ColorByGliter : MonoBehaviour
{
    #region variables

    public Material maskTexMaterial;
    private Texture2D maskTex;
    public List<Sprite> maskTexList;
   
    public static int maskTexIndex = -1;
    public static string ID = "0";


    // list of drawmodes
    public enum DrawMode
    {
        Pencil,
        Marker,
        PaintBucket,
        Sticker
    }

    //	*** Default settings ***
    private Color32 paintColor = new Color32(255, 0, 0, 255);
    [SerializeField]
    private int brushSize = 8; 
    private DrawMode drawMode = DrawMode.Pencil;
    private bool useLockArea = true;
    private byte[] lockMaskPixels; 

    private int selectedSticker = 0; 
    
    private int texWidthMinusStickerWidth;
    private int texHeightMinusStickerHeight;

  
  
    private int texWidthMinusPatternWidth;
    private int texHeightMinusPatternHeight;

    // UNDO
    private List<byte[]> undoPixels; 
    private int redoIndex = 0;
    private int RedoIndex
    {
        set
        {
            redoIndex = value;
        }

        get
        {
            return redoIndex;
        }
    }


    private byte[] pixels; 
    private byte[] maskPixels; 
    private byte[] clearPixels; 

    private Texture2D tex; 
    
    public int texWidth = 576;
    
    public int texHeight = 1024;
    private RaycastHit hit;
    private bool wentOutside = false;

    private Vector2 pixelUV; 
    private Vector2 pixelUVOld;

    private bool textureNeedsUpdate = false;

    [Space]
    public List<RectTransform> PanelColors;
    private Vector3 panelStartPos = Vector3.zero, panelEndPos = Vector3.zero;

    public List<PaintingButton> drawModeButton;
    [System.Serializable]
    public class PaintingButton
    {
        public string name;
    }

    
    #endregion


    #region Init And Control Functions
   private void Awake()
    {
        if (maskTexIndex < 0)
        {
            maskTex = null;
        }
        else
        {
            maskTex = DuplicateTexture(maskTexList[maskTexIndex].texture);
        }

        InitializeEverything();
    }

      private void Start()
    {
#if UNITY_ANDROID
        if (JavadRastadAndroidRuntimePermissions.CheckDeniedStoragePermissions())
        {
           
        }
#endif
        SetPanelsUIScale((int)DrawMode.Pencil);

        OnDrawModeButtonClicked((int)DrawMode.Pencil);

        OnBrushButtonClicked(PanelColors[(int)drawMode].GetChild(0).GetComponent<ButtonScript>());

        OnChangeBrushSizeButtonClicked();
    }
    private Texture2D DuplicateTexture(Texture2D source)
  {
    RenderTexture renderTex = RenderTexture.GetTemporary(
        source.width,
        source.height,
        0,
        RenderTextureFormat.Default,
        RenderTextureReadWrite.Linear);
    Graphics.Blit(source, renderTex);
    RenderTexture previous = RenderTexture.active;
    RenderTexture.active = renderTex;
    Texture2D readableText = new Texture2D(source.width, source.height);
    readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
    readableText.Apply();
    RenderTexture.active = previous;
    RenderTexture.ReleaseTemporary(renderTex);
    
    return readableText;
}


    private void InitializeEverything()
    {
        CreateFullScreenQuad();

        // create texture
        if (maskTex)
        {
            GetComponent<Image>().material = maskTexMaterial;

            texWidth = maskTex.width;
            texHeight = maskTex.height;
            GetComponent<Image>().material.SetTexture("_MaskTex", maskTex);

            useLockArea = true;
        }
        else
        {
            texWidth = 576;
            texHeight = 1024;

            useLockArea = false;
        }

        if (!GetComponent<Image>().material.HasProperty("_MainTex")) Debug.LogError("Fatal error: Current shader doesn't have a property: '_MainTex'");


        // create new texture
        tex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
        GetComponent<Image>().material.SetTexture("_MainTex", tex);

        pixels = new byte[texWidth * texHeight * 4];

        OnClearButtonClicked();

        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;

        if (maskTex)
        {
            ReadMaskImage();
        }

        // undo system
        undoPixels = new List<byte[]>();
        undoPixels.Add(new byte[texWidth * texHeight * 4]);
        RedoIndex = 0;

        byte[] loadPixels = new byte[texWidth * texHeight * 4];
        loadPixels = LoadImage(ID);

        if (loadPixels != null)
        {
            pixels = loadPixels;
            System.Array.Copy(pixels, undoPixels[0], pixels.Length);

            tex.LoadRawTextureData(pixels);
            tex.Apply(false);
        }
        else
        {
            System.Array.Copy(pixels, undoPixels[0], pixels.Length);
        }

        // locking mask enabled
        if (useLockArea)
        {
            lockMaskPixels = new byte[texWidth * texHeight * 4];
        }
    }
    private void CreateFullScreenQuad()
    {
        Image image = GetComponent<Image>();
        if (image == null)
        {
            Debug.LogError("No Image component found! Please attach this script to a UI Image.");
            return;
        }

        RectTransform rectTransform = image.rectTransform;
       
        image.preserveAspect = true;
        if (image.sprite != null && image.sprite.texture != null)
        {
            int textureWidth = image.sprite.texture.width;
            int textureHeight = image.sprite.texture.height;
            Debug.Log($"Texture Width: {textureWidth}, Texture Height: {textureHeight}");
        }
        else
        {
            Debug.LogWarning("No texture found on the Image component.");
        }
    
    }

    private void ReadMaskImage()
    {
        maskPixels = new byte[texWidth * texHeight * 4];

        int pixel = 0;
        for (int y = 0; y < texHeight; y++)
        {
            for (int x = 0; x < texWidth; x++)
            {
                Color c = maskTex.GetPixel(x, y);
                maskPixels[pixel] = (byte)(c.r * 255);
                maskPixels[pixel + 1] = (byte)(c.g * 255);
                maskPixels[pixel + 2] = (byte)(c.b * 255);
                maskPixels[pixel + 3] = (byte)(c.a * 255);
                pixel += 4;
            }
        }
    }

    private byte[] LoadImage(string key)
    {
#if UNITY_WEBGL
        string file = Application.persistentDataPath + "/Portrait" + key + ".sav";
        if (File.Exists(file))
        {
            return System.Convert.FromBase64String(File.ReadAllText(file));
        }
        else
        {
            return null;
        }
#else
        if (PlayerPrefs.HasKey(key))
        {
            return System.Convert.FromBase64String(PlayerPrefs.GetString(key));
        }
        else
        {
            return null;
        }
#endif
    }

private void SaveImage(string key)
{
    if (pixels == null || pixels.Length != texWidth * texHeight * 4)
    {
        Debug.LogError("Invalid pixel data.");
        return;
    }

#if UNITY_WEBGL
    string file = Application.persistentDataPath + "/Portrait" + key + ".sav";
    string fileData = System.Convert.ToBase64String(pixels);
    File.WriteAllText(file, fileData);
#else
    PlayerPrefs.SetString(key, System.Convert.ToBase64String(pixels));
    PlayerPrefs.Save();
#endif

}
  

    private void SetPanelsUIScale(int current)
    {
    }

    private void LateUpdate()
    {
        MousePaint();

        UpdateTexture();
    }

    private void MousePaint()
{
    if (Input.GetMouseButtonDown(0) || Input.GetMouseButton(0))
    {
        RaycastHit hit;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out hit))
        {
            if (hit.collider == null || !hit.collider.gameObject.name.Contains("PaintingBoard"))
            {
                return;
            }
        }
        else
        {
            RaycastHit2D hit2D = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero);

            if (hit2D.collider == null || !hit2D.collider.gameObject.name.Contains("PaintingBoard"))
            {
                return;
            }
            pixelUVOld = pixelUV;
            pixelUV = hit2D.point;
            pixelUV.x = (pixelUV.x - hit2D.collider.bounds.min.x) / hit2D.collider.bounds.size.x * texWidth;
            pixelUV.y = (pixelUV.y - hit2D.collider.bounds.min.y) / hit2D.collider.bounds.size.y * texHeight;
            pixelUV.x = Mathf.Clamp(pixelUV.x, 0, texWidth - 1);
            pixelUV.y = Mathf.Clamp(pixelUV.y, 0, texHeight - 1);

        }
    }

    if (Input.GetMouseButtonDown(0))
    {
        if (useLockArea)
        {
            if (!Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit, Mathf.Infinity, 1))
            {
                RaycastHit2D hit2D = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero);
                if (hit2D.collider == null) return;

                // Convert world position to texture UV coordinates
                pixelUV.x = (hit2D.point.x - hit2D.collider.bounds.min.x) / hit2D.collider.bounds.size.x * texWidth;
                pixelUV.y = (hit2D.point.y - hit2D.collider.bounds.min.y) / hit2D.collider.bounds.size.y * texHeight;

                // 🔥 Strict Clamping Before Using pixelUV
                pixelUV.x = Mathf.Clamp(pixelUV.x, 0, texWidth - 1);
                pixelUV.y = Mathf.Clamp(pixelUV.y, 0, texHeight - 1);

                CreateAreaLockMask((int)pixelUV.x, (int)pixelUV.y);
            }
            else
            {
                CreateAreaLockMask((int)(hit.textureCoord.x * texWidth), (int)(hit.textureCoord.y * texHeight));
            }
        }

        if (!Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit, Mathf.Infinity, 1))
        {
            RaycastHit2D hit2D = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero);
            if (hit2D.collider == null) { wentOutside = true; return; }

            pixelUVOld = pixelUV;
            pixelUV = hit2D.point;
            pixelUV.x = (pixelUV.x - hit2D.collider.bounds.min.x) / hit2D.collider.bounds.size.x * texWidth;
            pixelUV.y = (pixelUV.y - hit2D.collider.bounds.min.y) / hit2D.collider.bounds.size.y * texHeight;
        }
        else
        {
            pixelUVOld = pixelUV;
            pixelUV = hit.textureCoord;
            pixelUV.x *= texWidth;
            pixelUV.y *= texHeight;
        }
        pixelUV.x = Mathf.Clamp(pixelUV.x, 0, texWidth - 1);
        pixelUV.y = Mathf.Clamp(pixelUV.y, 0, texHeight - 1);

        if (wentOutside) { pixelUVOld = pixelUV; wentOutside = false; }
        textureNeedsUpdate = true;
    }

    if (Input.GetMouseButtonDown(0) || Input.GetMouseButton(0))
    {
        Debug.Log($"[DEBUG] Mouse Painting at {pixelUV.x}, {pixelUV.y}");

        if (!Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit, Mathf.Infinity, 1))
        {
            RaycastHit2D hit2D = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero);
            if (hit2D.collider == null) { wentOutside = true; return; }

            pixelUVOld = pixelUV;
            pixelUV = hit2D.point;
            pixelUV.x = (pixelUV.x - hit2D.collider.bounds.min.x) / hit2D.collider.bounds.size.x * texWidth;
            pixelUV.y = (pixelUV.y - hit2D.collider.bounds.min.y) / hit2D.collider.bounds.size.y * texHeight;
        }
        else
        {
            pixelUVOld = pixelUV;
            pixelUV = hit.textureCoord;
            pixelUV.x *= texWidth;
            pixelUV.y *= texHeight;
        }
        pixelUV.x = Mathf.Clamp(pixelUV.x, 0, texWidth - 1);
        pixelUV.y = Mathf.Clamp(pixelUV.y, 0, texHeight - 1);

        Debug.Log($"[DEBUG] Final Clamped UV: {pixelUV.x}, {pixelUV.y}");

        if (wentOutside) { pixelUVOld = pixelUV; wentOutside = false; }

        Debug.Log($"[DEBUG] Performing Draw Operation at {pixelUV.x}, {pixelUV.y}");

        switch (drawMode)
        {
            case DrawMode.Pencil:
                DrawCircle((int)pixelUV.x, (int)pixelUV.y);
                break;
            
            default:
                break;
        }

        textureNeedsUpdate = true;
    }
}
    private void CreateAreaLockMask(int x, int y)
    {
        if (maskTex)
        {
            LockAreaFillWithThresholdMaskOnly(x, y);
        }
        else
        {
            LockMaskFillWithThreshold(x, y);
        }
    }

    private void LockAreaFillWithThresholdMaskOnly(int x, int y)
    {
        byte hitColorR = maskPixels[((texWidth * (y) + x) * 4) + 0];
        byte hitColorG = maskPixels[((texWidth * (y) + x) * 4) + 1];
        byte hitColorB = maskPixels[((texWidth * (y) + x) * 4) + 2];
        byte hitColorA = maskPixels[((texWidth * (y) + x) * 4) + 3];

        Queue<int> fillPointX = new Queue<int>();
        Queue<int> fillPointY = new Queue<int>();
        fillPointX.Enqueue(x);
        fillPointY.Enqueue(y);

        int ptsx, ptsy;
        int pixel = 0;

        lockMaskPixels = new byte[texWidth * texHeight * 4];

        while (fillPointX.Count > 0)
        {

            ptsx = fillPointX.Dequeue();
            ptsy = fillPointY.Dequeue();

            if (ptsy - 1 > -1)
            {
                pixel = (texWidth * (ptsy - 1) + ptsx) * 4; // down

                if (lockMaskPixels[pixel] == 0 // 
                    && (CompareThreshold(maskPixels[pixel + 0], hitColorR)) // 
                    && (CompareThreshold(maskPixels[pixel + 1], hitColorG))
                    && (CompareThreshold(maskPixels[pixel + 2], hitColorB))
                    && (CompareThreshold(maskPixels[pixel + 3], hitColorA)))
                {
                    fillPointX.Enqueue(ptsx);
                    fillPointY.Enqueue(ptsy - 1);
                    lockMaskPixels[pixel] = 1;
                }
            }

            if (ptsx + 1 < texWidth)
            {
                pixel = (texWidth * ptsy + ptsx + 1) * 4; // right
                if (lockMaskPixels[pixel] == 0
                    && (CompareThreshold(maskPixels[pixel + 0], hitColorR))
                    && (CompareThreshold(maskPixels[pixel + 1], hitColorG))
                    && (CompareThreshold(maskPixels[pixel + 2], hitColorB))
                    && (CompareThreshold(maskPixels[pixel + 3], hitColorA)))
                {
                    fillPointX.Enqueue(ptsx + 1);
                    fillPointY.Enqueue(ptsy);
                    lockMaskPixels[pixel] = 1;
                }
            }

            if (ptsx - 1 > -1)
            {
                pixel = (texWidth * ptsy + ptsx - 1) * 4; // left
                if (lockMaskPixels[pixel] == 0
                    && (CompareThreshold(maskPixels[pixel + 0], hitColorR)) 
                    && (CompareThreshold(maskPixels[pixel + 1], hitColorG))
                    && (CompareThreshold(maskPixels[pixel + 2], hitColorB))
                    && (CompareThreshold(maskPixels[pixel + 3], hitColorA)))
                {
                    fillPointX.Enqueue(ptsx - 1);
                    fillPointY.Enqueue(ptsy);
                    lockMaskPixels[pixel] = 1;
                }
            }

            if (ptsy + 1 < texHeight)
            {
                pixel = (texWidth * (ptsy + 1) + ptsx) * 4; // up
                if (lockMaskPixels[pixel] == 0
                    && (CompareThreshold(maskPixels[pixel + 0], hitColorR)) // if pixel is same as hit color OR same as paint color
                    && (CompareThreshold(maskPixels[pixel + 1], hitColorG))
                    && (CompareThreshold(maskPixels[pixel + 2], hitColorB))
                    && (CompareThreshold(maskPixels[pixel + 3], hitColorA)))
                {
                    fillPointX.Enqueue(ptsx);
                    fillPointY.Enqueue(ptsy + 1);
                    lockMaskPixels[pixel] = 1;
                }
            }
        }
    }

    private void LockMaskFillWithThreshold(int x, int y)
    {
   
        byte hitColorR = pixels[((texWidth * (y) + x) * 4) + 0];
        byte hitColorG = pixels[((texWidth * (y) + x) * 4) + 1];
        byte hitColorB = pixels[((texWidth * (y) + x) * 4) + 2];
        byte hitColorA = pixels[((texWidth * (y) + x) * 4) + 3];

        Queue<int> fillPointX = new Queue<int>();
        Queue<int> fillPointY = new Queue<int>();
        fillPointX.Enqueue(x);
        fillPointY.Enqueue(y);

        int ptsx, ptsy;
        int pixel = 0;

        lockMaskPixels = new byte[texWidth * texHeight * 4];

        while (fillPointX.Count > 0)
        {

            ptsx = fillPointX.Dequeue();
            ptsy = fillPointY.Dequeue();

            if (ptsy - 1 > -1)
            {
                pixel = (texWidth * (ptsy - 1) + ptsx) * 4; // down

                if (lockMaskPixels[pixel] == 0
                    && (CompareThreshold(pixels[pixel + 0], hitColorR) || CompareThreshold(pixels[pixel + 0], paintColor.r)) 
                    && (CompareThreshold(pixels[pixel + 1], hitColorG) || CompareThreshold(pixels[pixel + 1], paintColor.g))
                    && (CompareThreshold(pixels[pixel + 2], hitColorB) || CompareThreshold(pixels[pixel + 2], paintColor.b))
                    && (CompareThreshold(pixels[pixel + 3], hitColorA) || CompareThreshold(pixels[pixel + 3], paintColor.a)))
                {
                    fillPointX.Enqueue(ptsx);
                    fillPointY.Enqueue(ptsy - 1);
                    lockMaskPixels[pixel] = 1;
                }
            }

            if (ptsx + 1 < texWidth)
            {
                pixel = (texWidth * ptsy + ptsx + 1) * 4; 
                if (lockMaskPixels[pixel] == 0
                    && (CompareThreshold(pixels[pixel + 0], hitColorR) || CompareThreshold(pixels[pixel + 0], paintColor.r))
                    && (CompareThreshold(pixels[pixel + 1], hitColorG) || CompareThreshold(pixels[pixel + 1], paintColor.g))
                    && (CompareThreshold(pixels[pixel + 2], hitColorB) || CompareThreshold(pixels[pixel + 2], paintColor.b))
                    && (CompareThreshold(pixels[pixel + 3], hitColorA) || CompareThreshold(pixels[pixel + 3], paintColor.a)))
                {
                    fillPointX.Enqueue(ptsx + 1);
                    fillPointY.Enqueue(ptsy);
                    lockMaskPixels[pixel] = 1;
                }
            }

            if (ptsx - 1 > -1)
            {
                pixel = (texWidth * ptsy + ptsx - 1) * 4;
                if (lockMaskPixels[pixel] == 0
                    && (CompareThreshold(pixels[pixel + 0], hitColorR) || CompareThreshold(pixels[pixel + 0], paintColor.r)) 
                    && (CompareThreshold(pixels[pixel + 1], hitColorG) || CompareThreshold(pixels[pixel + 1], paintColor.g))
                    && (CompareThreshold(pixels[pixel + 2], hitColorB) || CompareThreshold(pixels[pixel + 2], paintColor.b))
                    && (CompareThreshold(pixels[pixel + 3], hitColorA) || CompareThreshold(pixels[pixel + 3], paintColor.a)))
                {
                    fillPointX.Enqueue(ptsx - 1);
                    fillPointY.Enqueue(ptsy);
                    lockMaskPixels[pixel] = 1;
                }
            }

            if (ptsy + 1 < texHeight)
            {
                pixel = (texWidth * (ptsy + 1) + ptsx) * 4; 
                if (lockMaskPixels[pixel] == 0
                    && (CompareThreshold(pixels[pixel + 0], hitColorR) || CompareThreshold(pixels[pixel + 0], paintColor.r))
                    && (CompareThreshold(pixels[pixel + 1], hitColorG) || CompareThreshold(pixels[pixel + 1], paintColor.g))
                    && (CompareThreshold(pixels[pixel + 2], hitColorB) || CompareThreshold(pixels[pixel + 2], paintColor.b))
                    && (CompareThreshold(pixels[pixel + 3], hitColorA) || CompareThreshold(pixels[pixel + 3], paintColor.a)))
                {
                    fillPointX.Enqueue(ptsx);
                    fillPointY.Enqueue(ptsy + 1);
                    lockMaskPixels[pixel] = 1;
                }
            }
        }
    }

    private void UpdateTexture()
    {
        if (textureNeedsUpdate)
        {
            textureNeedsUpdate = false;
            tex.LoadRawTextureData(pixels);
            tex.Apply(false);
        }
    }

    #endregion


    #region OnButtonsClicked

    public void OnDrawModeButtonClicked(int drawModeIndex)
    {
       

        int currentDrawMode = (int)drawMode;

        if (currentDrawMode == drawModeIndex)
            return;

        SetPanelsUIScale(currentDrawMode);

        drawMode = (DrawMode)drawModeIndex;
    }

    public void OnBrushButtonClicked(ButtonScript sender)
    {

        if (PanelColors == null )
        {
            Debug.LogError("PanelColors is not initialized or has no elements.");
            return;
        }

     // paintColor = sender.transform.GetChild(0).gameObject.GetComponent<Image>().color;
     if (sender == null)
    {
        Debug.LogError("Sender is null in OnBrushButtonClicked.");
        return;
    }

    if (PanelColors == null || PanelColors.Count <= (int)DrawMode.Pencil)
    {
        Debug.LogError("PanelColors list is not initialized or has missing elements.");
        return;
    }

            // paintColor = sender.GetComponent<Image>().color;

            paintColor = sender.transform.GetChild(0).GetComponent<Image>().color;

      Debug.Log(" paintColor : " + paintColor);
    
        switch (drawMode)
        {
            case DrawMode.Pencil:
            case DrawMode.Marker:
            case DrawMode.PaintBucket:

                int selectedNumber = sender.transform.GetSiblingIndex();
                float yOffsetPixels = 20;
                for (int i = 0; i < PanelColors[(int)DrawMode.Pencil].childCount; i++)
                {
                    Vector2 min = PanelColors[(int)DrawMode.Pencil].GetChild(i).GetComponent<RectTransform>().anchorMin;
                    Vector2 max = PanelColors[(int)DrawMode.Pencil].GetChild(i).GetComponent<RectTransform>().anchorMax;
                    RectTransform rt = PanelColors[(int)DrawMode.Pencil].GetChild(i).GetComponent<RectTransform>();
                    Image img = PanelColors[(int)DrawMode.Pencil].GetChild(i).GetComponent<Image>();

                    if (i == selectedNumber)
                    {
                     
                        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y + yOffsetPixels);
                         if (img != null)
                         img.raycastTarget = false;
                    }
                    else
                    {
                       rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -52.5f);
                        if (img != null)
                         img.raycastTarget = true;
                    }
                }
                break;
        }
    }

    public void OnChangeBrushSizeButtonClicked()
    {
        brushSize += 8;

        if (brushSize > 24)
        {
            brushSize = 8;
        }
    }

    public void OnUndoButtonClicked()
    {
        if (undoPixels.Count - RedoIndex - 1 > 0)
        {
            System.Array.Copy(undoPixels[undoPixels.Count - RedoIndex - 2], pixels, undoPixels[undoPixels.Count - RedoIndex - 2].Length);
            tex.LoadRawTextureData(undoPixels[undoPixels.Count - RedoIndex - 2]);
            tex.Apply(false);

            RedoIndex++;
        }
    }

    public void OnRedoButtonClicked()
    {
        if (undoPixels.Count > 0 && RedoIndex > 0)
        {
            System.Array.Copy(undoPixels[undoPixels.Count - RedoIndex], pixels, undoPixels[undoPixels.Count - RedoIndex].Length);
            tex.LoadRawTextureData(undoPixels[undoPixels.Count - RedoIndex]);
            tex.Apply(false);

            RedoIndex--;
        }
    }

    public void OnClearButtonClicked()
    {
        int pixel = 0;
        for (int y = 0; y < texHeight; y++)
        {
            for (int x = 0; x < texWidth; x++)
            {
                pixels[pixel] = 255;
                pixels[pixel + 1] = 255;
                pixels[pixel + 2] = 255;
                pixels[pixel + 3] = 255;
                pixel += 4;
            }
        }
        tex.LoadRawTextureData(pixels);
        tex.Apply(false);

        if (undoPixels != null)
        {
            if (RedoIndex > 0)
            {
                undoPixels.RemoveRange(undoPixels.Count - RedoIndex, RedoIndex);
                RedoIndex = 0;
            }

            undoPixels.Add(new byte[texWidth * texHeight * 4]);
            System.Array.Copy(pixels, undoPixels[undoPixels.Count - 1], pixels.Length);
        }
    }

    public void OnScreenshotButtonClicked()
    {
        StartCoroutine(OnSavePictureClickListener());
    }

    private IEnumerator OnSavePictureClickListener()
    {
#if UNITY_ANDROID
        if (JavadRastadAndroidRuntimePermissions.RequestStoragePermissions())
        {
#endif
        MusicController.USE.PlaySound(MusicController.USE.cameraSound);

        StartCoroutine(ScreenshotManager.SaveForPaint("MyPicture", "ColoringBook"));
        yield return new WaitForSeconds(1f);
#if UNITY_ANDROID
        }
     
#endif

        yield return null;
    }

    public void OnMusicControllerButtonClicked()
    {
        MusicController.USE.ChangeMusicSetting();
    }


  public void OnHomeButtonClicked()
{
    SaveImage(ID);
    StartCoroutine(DelayLoadMainScene());
}

private IEnumerator DelayLoadMainScene()
{
    yield return new WaitForSeconds(0f);
    SceneManager.LoadScene("MainScene");
    SceneManager.sceneLoaded += OnSceneLoaded;
}
private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
{
    if (scene.name == "MainScene")
    {
        UIManager.Instance.ReturnToPreviousScreen();

    }
}
    #endregion


    #region Painting Functions

    private void DrawCircle(int x, int y)
    {
        int pixel = 0;

        // draw fast circle: 
        int r2 = brushSize * brushSize;
        int area = r2 << 2;
        int rr = brushSize << 1;
        for (int i = 0; i < area; i++)
        {
            int tx = (i % rr) - brushSize;
            int ty = (i / rr) - brushSize;
            if (tx * tx + ty * ty < r2)
            {
                if (x + tx < 0 || y + ty < 0 || x + tx >= texWidth || y + ty >= texHeight) continue;

                pixel = (texWidth * (y + ty) + x + tx) * 4;

                if (!useLockArea || (useLockArea && lockMaskPixels[pixel] == 1))
                {
                    pixels[pixel] = paintColor.r;
                    pixels[pixel + 1] = paintColor.g;
                    pixels[pixel + 2] = paintColor.b;
                    pixels[pixel + 3] = paintColor.a;
                }

            }
        }
    }

    private void DrawAdditiveCircle(int x, int y)
    {
        int pixel = 0;

        // draw fast circle: 
        int r2 = brushSize * brushSize;
        int area = r2 << 2;
        int rr = brushSize << 1;
        for (int i = 0; i < area; i++)
        {
            int tx = (i % rr) - brushSize;
            int ty = (i / rr) - brushSize;
            if (tx * tx + ty * ty < r2)
            {
                if (x + tx < 0 || y + ty < 0 || x + tx >= texWidth || y + ty >= texHeight) continue;

                pixel = (texWidth * (y + ty) + x + tx) * 4;

                // additive over white also
                if (!useLockArea || (useLockArea && lockMaskPixels[pixel] == 1))
                {
                    pixels[pixel] = (byte)Mathf.Lerp(pixels[pixel], paintColor.r, paintColor.a / 255f * 0.1f);
                    pixels[pixel + 1] = (byte)Mathf.Lerp(pixels[pixel + 1], paintColor.g, paintColor.a / 255f * 0.1f);
                    pixels[pixel + 2] = (byte)Mathf.Lerp(pixels[pixel + 2], paintColor.b, paintColor.a / 255f * 0.1f);
                    pixels[pixel + 3] = (byte)Mathf.Lerp(pixels[pixel + 3], paintColor.a, paintColor.a / 255 * 0.1f);
                }

            }
        }
    }

    private bool CompareThreshold(byte a, byte b)
    {
        if (a < b)
        {
            a ^= b; b ^= a; a ^= b;
        }

        return (a - b) <= 128;
    }

    private void DrawPoint(int pixel)
    {
        pixels[pixel] = paintColor.r;
        pixels[pixel + 1] = paintColor.g;
        pixels[pixel + 2] = paintColor.b;
        pixels[pixel + 3] = paintColor.a;
    }

    private void DrawLine(Vector2 start, Vector2 end)
    {
        int x0 = (int)start.x;
        int y0 = (int)start.y;
        int x1 = (int)end.x;
        int y1 = (int)end.y;
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx, sy;
        if (x0 < x1) { sx = 1; } else { sx = -1; }
        if (y0 < y1) { sy = 1; } else { sy = -1; }
        int err = dx - dy;
        bool loop = true;
        int minDistance = (int)(brushSize >> 1);
        int pixelCount = 0;
        int e2;
        while (loop)
        {
            pixelCount++;
            if (pixelCount > minDistance)
            {
                pixelCount = 0;
                DrawCircle(x0, y0);
            }
            if ((x0 == x1) && (y0 == y1)) loop = false;
            e2 = 2 * err;
            if (e2 > -dy)
            {
                err = err - dy;
                x0 = x0 + sx;
            }
            if (e2 < dx)
            {
                err = err + dx;
                y0 = y0 + sy;
            }
        }
    }

    private void DrawAdditiveLine(Vector2 start, Vector2 end)
    {
        int x0 = (int)start.x;
        int y0 = (int)start.y;
        int x1 = (int)end.x;
        int y1 = (int)end.y;
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx, sy;
        if (x0 < x1) { sx = 1; } else { sx = -1; }
        if (y0 < y1) { sy = 1; } else { sy = -1; }
        int err = dx - dy;
        bool loop = true;
        int minDistance = (int)(brushSize >> 1);
        int pixelCount = 0;
        int e2;
        while (loop)
        {
            pixelCount++;
            if (pixelCount > minDistance)
            {
                pixelCount = 0;
                DrawAdditiveCircle(x0, y0);
            }
            if ((x0 == x1) && (y0 == y1)) loop = false;
            e2 = 2 * err;
            if (e2 > -dy)
            {
                err = err - dy;
                x0 = x0 + sx;
            }
            if (e2 < dx)
            {
                err = err + dx;
                y0 = y0 + sy;
            }
        }
    }

    
    #endregion

}