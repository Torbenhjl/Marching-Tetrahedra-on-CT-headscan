using System;
using System.Collections.Generic;
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

    List<Vector3> vertices = new List<Vector3>();
    List<int> indices = new List<int>();
    int indiceNum = 0;
    List<Slice> slices = new List<Slice>();

    int xdim = 100;
    int ydim = 100;
    int zdim = 100;

    float[] field;

    int idx(int x, int y, int z) => x + xdim * (y + ydim * z);

    public float iso = 0.0f;
    private Button _button;
    private Toggle _toggle;
    private Slider _slider1;

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

        slices.Add(_slices[0]);
        slices.Add(_slices[0]);
        //set3DTexture();
        BuildSphereField(0.6f);
        // setTexture(slices);
        doMarching3D();
        // iso = 0f; // IMPORTANT for SDF sphere
        meshScript mscript = GameObject.Find("GameObjectMesh").GetComponent<meshScript>();
        mscript.createMeshGeometry(vertices, indices);
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

    void BuildSphereField(float radiusN = 0.6f) // radius in normalized [-1,1] coordinates
    {
        field = new float[xdim * ydim * zdim];

        float cx = (xdim - 1) * 0.5f;
        float cy = (ydim - 1) * 0.5f;
        float cz = (zdim - 1) * 0.5f;

        float sx = cx; // scale so edges map to [-1,1]
        float sy = cy;
        float sz = cz;

        for (int z = 0; z < zdim; z++)
        for (int y = 0; y < ydim; y++)
        for (int x = 0; x < xdim; x++)
        {
            float xn = (x - cx) / sx;
            float yn = (y - cy) / sy;
            float zn = (z - cz) / sz;

            float r = Mathf.Sqrt(xn * xn + yn * yn + zn * zn);
            field[idx(x, y, z)] = r - radiusN; // isoLevel = 0 gives sphere surface
        }
    }

    void setTexture(List<Slice> slices)
    {
        var texture = new Texture2D(xdim, ydim, TextureFormat.RGB24, false); // garbage collector will tackle that it is new'ed

        for (int y = 0; y < ydim; y++)
        {
            for (int x = 0; x < xdim; x++)
            {
                float xn = (float)(x - xdim / 2) / (float)(xdim / 2);
                float yn = (float)(y - ydim / 2) / (float)(ydim / 2);
                float v = Math.Min(Mathf.Sqrt((xn * xn) + (yn * yn)), 1);

                texture.SetPixel(x, y, new UnityEngine.Color(v, v, v));
            }
        }

        doMarching(texture, slices);

        meshScript mscript = GameObject.Find("GameObjectMesh").GetComponent<meshScript>();
        mscript.createMeshGeometry(vertices, indices);
        texture.filterMode = FilterMode.Point; // nearest neigbor interpolation is used.  (alternative is FilterMode.Bilinear)
        texture.Apply(); // Apply all SetPixel calls
        GetComponent<Renderer>().material.mainTexture = texture;
    }

    void set3DTexture()
    {
        var Texture3D = new Texture3D(xdim, ydim, zdim, TextureFormat.RGB24, false);

        for (int x = 0; x < xdim; x++)
        {
            for (int y = 0; y < ydim; y++)
            {
                for (int z = 0; z < zdim; z++)
                {
                    float xn = (float)(x - xdim / 2) / (float)(xdim / 2);
                    float yn = (float)(y - ydim / 2) / (float)(ydim / 2);
                    float zn = (float)(z - zdim / 2) / (float)(zdim / 2);
                    float v = Math.Min(Mathf.Sqrt((xn * xn) + (yn * yn) + (zn * zn)), 1);

                    Texture3D.SetPixel(x, y, z, new UnityEngine.Color(v, v, v));
                }
            }
        }

        //meshScript mscript = GameObject.Find("3DMesh").GetComponent<meshScript>();
        // mscript.createMeshGeometry(vertices, indices);
        // Texture3D.filterMode = FilterMode.Point; // nearest neigbor interpolation is used.  (alternative is FilterMode.Bilinear)
        Texture3D.Apply(); // Apply all SetPixel calls
        var renderer = GetComponent<Renderer>();
        renderer.material.SetTexture("_VolumeTex", Texture3D); // assumes your shader has a _VolumeTex property of type 3D texture
        //GetComponent<Renderer>().material.mainTexture = Texture3D;
    }

    void doMarching(Texture2D texture, List<Slice> slices)
    {
        for (int y = 0; y < ydim - 1; y++)
        {
            for (int x = 0; x < xdim - 1; x++)
            {
                doCube(x, y, texture);
            }
        }
    }

    void doMarching3D()
    {
        for (int z = 0; z < zdim - 1; z++)
        for (int y = 0; y < ydim - 1; y++)
        for (int x = 0; x < xdim - 1; x++)
            doCube3D(x, y, z);
    }

    void doCube3D(int x, int y, int z)
    {
        // scalar values from the 3D field
        float v0 = field[idx(x, y, z)];
        float v1 = field[idx(x + 1, y, z)];
        float v2 = field[idx(x, y + 1, z)];
        float v3 = field[idx(x + 1, y + 1, z)];

        float v4 = field[idx(x, y, z + 1)];
        float v5 = field[idx(x + 1, y, z + 1)];
        float v6 = field[idx(x, y + 1, z + 1)];
        float v7 = field[idx(x + 1, y + 1, z + 1)];

        // positions in grid space (note: z is now real)
        Vector3 p0 = new Vector3(x, y, z);
        Vector3 p1 = new Vector3(x + 1, y, z);
        Vector3 p2 = new Vector3(x, y + 1, z);
        Vector3 p3 = new Vector3(x + 1, y + 1, z);

        Vector3 p4 = new Vector3(x, y, z + 1);
        Vector3 p5 = new Vector3(x + 1, y, z + 1);
        Vector3 p6 = new Vector3(x, y + 1, z + 1);
        Vector3 p7 = new Vector3(x + 1, y + 1, z + 1);

        // same tetra split you already use
        doTetra(p4, p6, p0, p7, v4, v6, v0, v7);
        doTetra(p6, p0, p7, p2, v6, v0, v7, v2);
        doTetra(p0, p7, p2, p3, v0, v7, v2, v3);
        doTetra(p4, p5, p7, p0, v4, v5, v7, v0);
        doTetra(p1, p7, p0, p3, v1, v7, v0, v3);
        doTetra(p0, p5, p7, p1, v0, v5, v7, v1);
    }

    void doCube(int x, int y, Texture2D texture)
    {
        // Verdiane.
        float v0 = texture.GetPixel(x, y).r;
        float v1 = texture.GetPixel(x + 1, y).r;
        float v2 = texture.GetPixel(x, y + 1).r;
        float v3 = texture.GetPixel(x + 1, y + 1).r;

        // Eg bruker bere samme pikslane, for me har ikkje oppretta fleire slices. Veit ikkje korleis ellers det skulle vert gjort.
        float v4 = texture.GetPixel(x, y).r;
        float v5 = texture.GetPixel(x + 1, y).r;
        float v6 = texture.GetPixel(x, y + 1).r;
        float v7 = texture.GetPixel(x + 1, y + 1).r;

        // Punktane.
        Vector3 p0 = new Vector3(x, y, 0);
        Vector3 p1 = new Vector3(x + 1, y, 0);
        Vector3 p2 = new Vector3(x, y + 1, 0);
        Vector3 p3 = new Vector3(x + 1, y + 1, 0);

        // andre slice
        Vector3 p4 = new Vector3(x, y, -1);
        Vector3 p5 = new Vector3(x + 1, y, -1);
        Vector3 p6 = new Vector3(x, y + 1, -1);
        Vector3 p7 = new Vector3(x + 1, y + 1, -1);

        doTetra(p4, p6, p0, p7, v4, v6, v0, v7);
        doTetra(p6, p0, p7, p2, v6, v0, v7, v2);
        doTetra(p0, p7, p2, p3, v0, v7, v2, v3);
        doTetra(p4, p5, p7, p0, v4, v5, v7, v0);
        doTetra(p1, p7, p0, p3, v1, v7, v0, v3);
        doTetra(p0, p5, p7, p1, v0, v5, v7, v1);
    }

    void doTetra(
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector3 p3,
        float v0,
        float v1,
        float v2,
        float v3
    )
    {
        bool[] inside = { v0 <= iso, v1 <= iso, v2 <= iso, v3 <= iso };
        Vector3[] p = { p0, p1, p2, p3 };
        float[] v = { v0, v1, v2, v3 };

        // count inside vertices
        int insideCount = 0;
        for (int i = 0; i < 4; i++)
            if (inside[i])
                insideCount++;

        if (insideCount == 0 || insideCount == 4)
            return;

        // helper to get iso point on an edge i-j
        Vector3 E(int i, int j) => calcIsoPos(p[i], p[j], v[i], v[j], iso);

        // witht the inside/outside count we can split into 3 cases:
        //1. all/none inside do nothing,
        //2.  1 inside -> 1 triangle, 3 inside -> 1 triangle (complement of 1-inside),
        //3. 2 inside -> quad (2 triangles)

        // Case 2: 1 inside -> 1 triangle
        if (insideCount == 1)
        {
            // int a =
            //     inside[0] ? 0
            //     : inside[1] ? 1
            //     : inside[2] ? 2
            //     : 3; // the inside vertex
            // int b = (a == 0) ? 1 : 0;
            // int c = (a == 2) ? 3 : 2;
            // int d = 6 - a - b - c; // remaining index (since 0+1+2+3=6)

            int outV = -1;
            for (int i = 0; i < 4; i++)
            {
                if (inside[i])
                {
                    outV = i;
                    break;
                }
            }
            List<int> outIdx = new List<int>(3);
            for (int i = 0; i < 4; i++)
                if (i != outV)
                    outIdx.Add(i);

            Vector3 pAB = E(outV, outIdx[0]);
            Vector3 pAC = E(outV, outIdx[1]);
            Vector3 pAD = E(outV, outIdx[2]);

            makeTri(pAB, pAC, pAD);
            return;
        }

        // Case 2: 3 inside -> 1 triangle (complement of 1-inside), reverse winding
        if (insideCount == 3)
        {
            int outV = -1;
            for (int i = 0; i < 4; i++)
                if (!inside[i])
                {
                    outV = i;
                    break;
                }

            List<int> inIdx = new List<int>(3);
            for (int i = 0; i < 4; i++)
                if (i != outV)
                    inIdx.Add(i);

            Vector3 pOA = E(outV, inIdx[0]);
            Vector3 pOB = E(outV, inIdx[1]);
            Vector3 pOC = E(outV, inIdx[2]);

            // reverse winding compared to 1-inside
            makeTri(pOA, pOC, pOB);
            return;
        }

        // Case 3: 2 inside, 2 outside -> quad (2 triangles)
        // Find the two inside vertices and two outside
        List<int> insideIdx = new List<int>(2);
        List<int> outsideIdx = new List<int>(2);
        for (int i = 0; i < 4; i++)
        {
            if (inside[i])
                insideIdx.Add(i);
            else
                outsideIdx.Add(i);
        }

        int i0 = insideIdx[0],
            i1 = insideIdx[1];
        int o0 = outsideIdx[0],
            o1 = outsideIdx[1];

        // Intersections: each inside connects to each outside -> 4 points
        Vector3 a0 = E(i0, o0);
        Vector3 a1 = E(i0, o1);
        Vector3 b0 = E(i1, o0);
        Vector3 b1 = E(i1, o1);

        // Build a quad. The correct pairing depends on which outside vertices match.
        // This ordering is consistent for a tetra:
        makeQuad(a0, b0, b1, a1); // uses your makeQuad -> (a0,b0,b1) + (a0,b1,a1)
    }

    // void doTetra(
    //     Vector3 p0,
    //     Vector3 p1,
    //     Vector3 p2,
    //     Vector3 p3,
    //     float v0,
    //     float v1,
    //     float v2,
    //     float v3
    // )
    // {
    //     bool b0 = v0 <= iso;
    //     bool b1 = v1 <= iso;
    //     bool b2 = v2 <= iso;
    //     bool b3 = v3 <= iso;
    //
    //     int index = 0;
    //
    //     if (b0)
    //         index |= 1;
    //     if (b1)
    //         index |= 2;
    //     if (b2)
    //         index |= 4;
    //     if (b3)
    //         index |= 8;
    //     switch (index)
    //     {
    //         // 0 and 15: nothing
    //
    //         case 1:
    //             makeTri(
    //                 calcIsoPos(p0, p3, v0, v3, iso),
    //                 calcIsoPos(p2, p3, v2, v3, iso),
    //                 calcIsoPos(p1, p3, v1, v3, iso)
    //             );
    //             break;
    //
    //         case 14: // reverse winding
    //             makeTri(
    //                 calcIsoPos(p0, p3, v0, v3, iso),
    //                 calcIsoPos(p1, p3, v1, v3, iso),
    //                 calcIsoPos(p2, p3, v2, v3, iso)
    //             );
    //             break;
    //
    //         case 2:
    //             makeTri(
    //                 calcIsoPos(p0, p2, v0, v2, iso),
    //                 calcIsoPos(p1, p2, v1, v2, iso),
    //                 calcIsoPos(p2, p3, v2, v3, iso)
    //             );
    //             break;
    //
    //         case 13: // reverse
    //             makeTri(
    //                 calcIsoPos(p0, p2, v0, v2, iso),
    //                 calcIsoPos(p2, p3, v2, v3, iso),
    //                 calcIsoPos(p1, p2, v1, v2, iso)
    //             );
    //             break;
    //
    //         case 3:
    //             makeQuad(
    //                 calcIsoPos(p0, p2, v0, v2, iso),
    //                 calcIsoPos(p1, p2, v1, v2, iso),
    //                 calcIsoPos(p1, p3, v1, v3, iso),
    //                 calcIsoPos(p0, p3, v0, v3, iso)
    //             );
    //             break;
    //
    //         case 12: // reverse quad order
    //             makeQuad(
    //                 calcIsoPos(p0, p2, v0, v2, iso),
    //                 calcIsoPos(p0, p3, v0, v3, iso),
    //                 calcIsoPos(p1, p3, v1, v3, iso),
    //                 calcIsoPos(p1, p2, v1, v2, iso)
    //             );
    //             break;
    //
    //         case 4:
    //             makeTri(
    //                 calcIsoPos(p0, p1, v0, v1, iso),
    //                 calcIsoPos(p1, p3, v1, v3, iso),
    //                 calcIsoPos(p1, p2, v1, v2, iso)
    //             );
    //             break;
    //
    //         case 11: // reverse
    //             makeTri(
    //                 calcIsoPos(p0, p1, v0, v1, iso),
    //                 calcIsoPos(p1, p2, v1, v2, iso),
    //                 calcIsoPos(p1, p3, v1, v3, iso)
    //             );
    //             break;
    //
    //         case 5:
    //             makeQuad(
    //                 calcIsoPos(p0, p1, v0, v1, iso),
    //                 calcIsoPos(p1, p2, v1, v2, iso),
    //                 calcIsoPos(p2, p3, v2, v3, iso),
    //                 calcIsoPos(p0, p3, v0, v3, iso)
    //             );
    //             break;
    //
    //         case 10: // reverse
    //             makeQuad(
    //                 calcIsoPos(p0, p1, v0, v1, iso),
    //                 calcIsoPos(p0, p3, v0, v3, iso),
    //                 calcIsoPos(p2, p3, v2, v3, iso),
    //                 calcIsoPos(p1, p2, v1, v2, iso)
    //             );
    //             break;
    //
    //         case 6:
    //             makeQuad(
    //                 calcIsoPos(p0, p1, v0, v1, iso),
    //                 calcIsoPos(p0, p2, v0, v2, iso),
    //                 calcIsoPos(p2, p3, v2, v3, iso),
    //                 calcIsoPos(p1, p3, v1, v3, iso)
    //             );
    //             break;
    //
    //         case 9: // reverse
    //             makeQuad(
    //                 calcIsoPos(p0, p1, v0, v1, iso),
    //                 calcIsoPos(p1, p3, v1, v3, iso),
    //                 calcIsoPos(p2, p3, v2, v3, iso),
    //                 calcIsoPos(p0, p2, v0, v2, iso)
    //             );
    //             break;
    //
    //         case 7:
    //             makeTri(
    //                 calcIsoPos(p0, p1, v0, v1, iso),
    //                 calcIsoPos(p0, p2, v0, v2, iso),
    //                 calcIsoPos(p0, p3, v0, v3, iso)
    //             );
    //             break;
    //
    //         case 8: // reverse
    //             makeTri(
    //                 calcIsoPos(p0, p1, v0, v1, iso),
    //                 calcIsoPos(p0, p3, v0, v3, iso),
    //                 calcIsoPos(p0, p2, v0, v2, iso)
    //             );
    //             break;
    //     }
    // }

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
        makeTri(p1, p2, p3);
        makeTri(p1, p3, p4);
    }

    // Vector3 calcIsoPos(Vector3 p0, Vector3 p1)
    // {
    //     Vector3 ans = (p0 + p1) / 2;
    //     ans.x = ans.x / xdim - 0.5f;
    //     ans.y = ans.y / ydim - 0.5f;
    //     ans.z = ans.z / 10;
    //     return ans;
    // }

    Vector3 interpolate(Vector3 p0, Vector3 p1, float v0, float v1)
    {
        float t = (iso - v0) / (v1 - v0);
        return p0 + t * (p1 - p0);
    }

    Vector3 calcIsoPos(Vector3 p0, Vector3 p1, float v0, float v1, float iso)
    {
        const float EPS = 1e-6f;

        // If values are (almost) equal, fall back to midpoint to avoid division by zero
        float dv = v1 - v0;
        float t = 0.5f;
        if (Mathf.Abs(dv) >= EPS)
        {
            t = (iso - v0) / dv;
            t = Mathf.Clamp01(t); // Ensure t is in [0,1] to avoid extrapolation
        }

        // Interpolate position in grid space
        Vector3 ans = p0 + t * (p1 - p0);

        // Convert grid -> local mesh space (same as your original function)
        ans.x = ans.x / (xdim - 1f) - 0.5f;
        ans.y = ans.y / (ydim - 1f) - 0.5f;
        ans.z = ans.z / (zdim - 1f) - 0.5f;

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
        iso = evt.newValue;

        vertices.Clear();
        indices.Clear();
        indiceNum = 0;

        setTexture(slices);
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
}
