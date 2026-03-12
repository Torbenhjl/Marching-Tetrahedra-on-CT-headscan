using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public class quadScript : MonoBehaviour
{
    // Dicom har et "levende" dictionary som leses fra xml ved initDicom
    // slices må sorteres, og det basert på en tag, men at pixeldata lesing er en separat operasjon, derfor har vi nullpeker til pixeldata
    // dicomfile lagres slik at fil ikke må leses enda en gang når pixeldata hentes

    // member variables of quadScript, accessible from any function
    Slice[] _slices;
    int _numSlices;
    int _minIntensity;
    int _maxIntensity;
    float isovalue = 0.9f;
    List<Vector3> vertices = new List<Vector3>();
    List<int> indices = new List<int>();
    int indiceNum = 0;
    List<Slice> slices = new List<Slice>();

    int xdim = 100;
    int ydim = 100;

    int zdim = 0;
    int width = 0;
    int height = 0;

    private Button _button;
    private Toggle _toggle;
    private Slider _slider1;

    //int _iso;

    // Use this for initialization
    void Start()
    {
        Renderer r = GetComponent<Renderer>();
        Material m = r.material;
        m.shader = Shader.Find("Unlit/Texture");

        var uiDocument = GameObject.Find("MyUIDocument").GetComponent<UIDocument>();
        _button = uiDocument.rootVisualElement.Q("button1") as Button;
        _toggle = uiDocument.rootVisualElement.Q("toggle") as Toggle;
        _slider1 = uiDocument.rootVisualElement.Q("slider1") as Slider;
        _button.RegisterCallback<ClickEvent>(button1Pushed);
        _slider1.RegisterValueChangedCallback(slicePosSliderChange);

        Slice.initDicom();

        string dicomfilepath = Application.dataPath + @"\..\dicomdata\"; // Application.dataPath is in the assets folder, but these files are "managed", so we go one level up

        _slices = processSlices(dicomfilepath); // loads slices from the folder above

        foreach (var slice in _slices)
        {
            slices.Add(slice);
        }
        width = slices[0].sliceInfo.Columns;
        height = slices[0].sliceInfo.Rows;
        zdim = slices.Count;

        setTexture(slices);
    }

    Slice[] processSlices(string dicomfilepath)
    {
        string[] dicomfilenames = Directory.GetFiles(dicomfilepath, "*.IMA");
        _numSlices = dicomfilenames.Length;

        Slice[] slices = new Slice[_numSlices];

        float max = -1;
        float min = 99999;
        for (int i = 0; i < _numSlices; i++)
        {
            string filename = dicomfilenames[i];
            slices[i] = new Slice(filename);
            SliceInfo info = slices[i].sliceInfo;
            if (info.LargestImagePixelValue > max)
                max = info.LargestImagePixelValue;
            if (info.SmallestImagePixelValue < min)
                min = info.SmallestImagePixelValue;
            // Del dataen på max før den settes inn i tekstur
            // alternativet er å dele på 2^dicombitdepth,  men det ville blitt 4096 i dette tilfelle
        }
        print("Number of slices read:" + _numSlices);
        print("Max intensity in all slices:" + max);
        print("Min intensity in all slices:" + min);

        _minIntensity = (int)min;
        _maxIntensity = (int)max;
        //_iso = 0;

        Array.Sort(slices);

        return slices;
    }

    void setTexture(List<Slice> slices)
    {
        // No longer useful. var texture = new Texture2D(xdim, ydim, TextureFormat.RGB24, false);     // garbage collector will tackle that it is new'ed
        /*
        for (int y = 0; y < ydim; y++)
        {
            for (int x = 0; x < xdim; x++)
            {
                float xn = (float)(x - xdim/2) / (float)(xdim/2);
                float yn = (float)(y - ydim/2) / (float)(ydim/2);
                float v = Math.Min(Mathf.Sqrt((xn * xn) + (yn * yn)), 1);

                texture.SetPixel(x, y, new UnityEngine.Color(v, v, v));
            }
        } */

        doMarching(slices);
        //makeAWallInCube(); feilsøking.

        meshScript mscript = GameObject.Find("GameObjectMesh").GetComponent<meshScript>();
        mscript.MeshToFile1("MarchingTetrahedraObject.obj", ref vertices, ref indices);
        print("Vertices count:" + vertices.Count);
        print("Indices count:" + indices.Count);
        //texture.filterMode = FilterMode.Point;  // nearest neigbor interpolation is used.  (alternative is FilterMode.Bilinear)
        //texture.Apply();  // Apply all SetPixel calls
        //GetComponent<Renderer>().material.mainTexture = texture;
    }

    void doMarching(List<Slice> slices)
    {
        width = slices[0].sliceInfo.Columns;
        height = slices[0].sliceInfo.Rows;
        zdim = slices.Count;

        xdim = width;
        ydim = height;

        // March one cube layer outside the real data
        for (int z = -1; z < zdim; z++)
        {
            for (int y = -1; y < height; y++)
            {
                for (int x = -1; x < width; x++)
                {
                    doCube(x, y, z);
                }
            }
        }
    }

    void doCube(int x, int y, int z)
    {
        // Corner values
        float v0 = SampleVolumePadded(x, y, z);
        float v1 = SampleVolumePadded(x + 1, y, z);
        float v2 = SampleVolumePadded(x, y + 1, z);
        float v3 = SampleVolumePadded(x + 1, y + 1, z);

        float v4 = SampleVolumePadded(x, y, z + 1);
        float v5 = SampleVolumePadded(x + 1, y, z + 1);
        float v6 = SampleVolumePadded(x, y + 1, z + 1);
        float v7 = SampleVolumePadded(x + 1, y + 1, z + 1);

        // Corner positions
        Vector3 p0 = new Vector3(x, y, z);
        Vector3 p1 = new Vector3(x + 1, y, z);
        Vector3 p2 = new Vector3(x, y + 1, z);
        Vector3 p3 = new Vector3(x + 1, y + 1, z);

        Vector3 p4 = new Vector3(x, y, z + 1);
        Vector3 p5 = new Vector3(x + 1, y, z + 1);
        Vector3 p6 = new Vector3(x, y + 1, z + 1);
        Vector3 p7 = new Vector3(x + 1, y + 1, z + 1);

        doTetra(p4, p6, p0, p7, v4, v6, v0, v7);
        doTetra(p6, p0, p7, p2, v6, v0, v7, v2);
        doTetra(p0, p7, p2, p3, v0, v7, v2, v3);
        doTetra(p4, p5, p7, p0, v4, v5, v7, v0);
        doTetra(p1, p7, p0, p3, v1, v7, v0, v3);
        doTetra(p0, p5, p7, p1, v0, v5, v7, v1);
    }

    void doTetra(
        Vector3 p1,
        Vector3 p2,
        Vector3 p3,
        Vector3 p4,
        float v1,
        float v2,
        float v3,
        float v4
    )
    {
        bool b1 = v1 >= isovalue;
        bool b2 = v2 >= isovalue;
        bool b3 = v3 >= isovalue;
        bool b4 = v4 >= isovalue;

        int index = 0;

        if (b1)
            index |= 1;
        if (b2)
            index |= 2;
        if (b3)
            index |= 4;
        if (b4)
            index |= 8;

        Vector3 p12 = calcIsoPos(p1, p2, v1, v2);
        Vector3 p13 = calcIsoPos(p1, p3, v1, v3);
        Vector3 p14 = calcIsoPos(p1, p4, v1, v4);
        Vector3 p23 = calcIsoPos(p2, p3, v2, v3);
        Vector3 p24 = calcIsoPos(p2, p4, v2, v4);
        Vector3 p34 = calcIsoPos(p3, p4, v3, v4);

        switch (index)
        {
            // ingen case 0 eller 15 siden dei ikkje gjør noko.
            case 1:
            case 14:
                makeTri(p12, p13, p14);
                break;

            case 2:
            case 13:
                makeTri(p12, p23, p24);
                break;

            case 3:
            case 12:
                makeQuad(p13, p14, p23, p24);
                break;

            case 4:
            case 11:
                makeTri(p13, p23, p34);
                break;

            case 5:
            case 10:
                makeQuad(p12, p23, p34, p14);
                break;

            case 6:
            case 9:
                makeQuad(p12, p13, p34, p24);
                break;

            case 7:
            case 8:
                makeTri(p14, p24, p34);
                break;
        }
    }

    void makeTri(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        vertices.Add(p1);
        vertices.Add(p2);
        vertices.Add(p3);
        indices.Add(indiceNum++);
        indices.Add(indiceNum++);
        indices.Add(indiceNum++);
    }

    void makeQuad(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
    {
        makeTri(p1, p2, p4);
        makeTri(p2, p3, p4);
    }

    Vector3 calcIsoPos(Vector3 p0, Vector3 p1, float v0, float v1)
    {
        if (Mathf.Abs(v1 - v0) < 1e-6f)
            return (p0 + p1) * 0.5f;

        float t = (isovalue - v0) / (v1 - v0);
        Vector3 ans = p0 + t * (p1 - p0);

        // Because we march from -1 to width/height/zdim,
        // shift by +1 and divide by padded size
        ans.x = (ans.x + 1f) / (width + 2f) - 0.5f;
        ans.y = (ans.y + 1f) / (height + 2f) - 0.5f;
        ans.z = (ans.z + 1f) / (zdim + 2f) - 0.5f;

        return ans;
    }

    ushort pixelval(Vector2 p, int xdim, ushort[] pixels)
    {
        return pixels[(int)p.x + (int)p.y * xdim];
    }

    Vector2 vec2(float x, float y)
    {
        return new Vector2(x, y);
    }

    // Update is called once per frame
    void Update() { }

    public void slicePosSliderChange(ChangeEvent<float> evt)
    {
        print("slicePosSliderChange:" + evt.newValue);
        isovalue = evt.newValue / 100;

        // vertices.Clear();
        // indices.Clear();
        // indiceNum = 0;
        //
        // setTexture(slices);
    }

    float SampleVolumePadded(int x, int y, int z)
    {
        // Outside the dataset = 0 to close holes at the borders
        if (x < 0 || x >= width || y < 0 || y >= height || z < 0 || z >= zdim)
            return 0f;

        Slice slice = slices[z];
        ushort[] pixels = slice.getPixels();

        int idx = x + y * width;
        ushort raw = pixels[idx];

        float denom = (_maxIntensity - _minIntensity) > 0 ? (_maxIntensity - _minIntensity) : 1f;
        return Mathf.Clamp01((raw - _minIntensity) / denom);
    }

    public void sliceIsoSliderChange(float val)
    {
        print("sliceIsoSliderChange:" + val);
    }

    public void button1Pushed(ClickEvent evt)
    {
        print("button1Pushed");
    }

    public void button2Pushed()
    {
        print("button2Pushed");
    }

    float SampleNormalized(ushort[] pixels, int x, int y, int width)
    {
        int idx = x + y * width;
        ushort raw = pixels[idx];
        float denom = (_maxIntensity - _minIntensity) > 0 ? (_maxIntensity - _minIntensity) : 1f;
        return Mathf.Clamp01((raw - _minIntensity) / denom);
    }
}
