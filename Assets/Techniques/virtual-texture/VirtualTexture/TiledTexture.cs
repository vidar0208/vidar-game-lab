using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualTexture
{
    // Port of the source's TiledTexture: the physical page cache, RegionSize.x x RegionSize.y tiles of
    // TileSize + 2 x PaddingSize pixels, recycled least-recently-used first (LruCache, unchanged).
    // GameTech changes: a tile is filled by drawing the page procedurally (my page shader) into its viewport
    // instead of blitting a loaded Texture2D; inputs written through VTBinding.
    public class TiledTexture : MonoBehaviour
    {
        public event Action<Vector2Int> OnTileUpdateComplete;

        [SerializeField] private Vector2Int m_RegionSize = new Vector2Int(8, 8);
        [SerializeField] private int m_TileSize = 256;
        [SerializeField] private int m_PaddingSize = 4;
        public Material pageMaterial;

        private LruCache m_TilePool = new LruCache();
        private CommandBuffer cmd;
        private MaterialPropertyBlock mpb;

        public RenderTexture Texture { get; private set; }
        public Vector2Int RegionSize { get { return m_RegionSize; } set { m_RegionSize = value; } }
        public int TileSize { get { return m_TileSize; } }
        public int PaddingSize { get { return m_PaddingSize; } }
        public int TileSizeWithPadding { get { return TileSize + PaddingSize * 2; } }
        public int TileCount => RegionSize.x * RegionSize.y;

        private void Start()
        {
            for (int i = 0; i < RegionSize.x * RegionSize.y; i++)
                m_TilePool.Add(i);
            Texture = new RenderTexture(RegionSize.x * TileSizeWithPadding, RegionSize.y * TileSizeWithPadding, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { useMipMap = false, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "VT tile cache" };
            Texture.Create();
            var binding = GetComponent<VTBinding>();
            binding.SetTexture("_VTTiledTex0", Texture);
            // x: padding offset, y: page area of a tile, zw: 1 / tile count
            binding.SetVector("_VTTileParam", new Vector4((float)PaddingSize / TileSizeWithPadding, (float)TileSize / TileSizeWithPadding, 1.0f / RegionSize.x, 1.0f / RegionSize.y));
            cmd = new CommandBuffer { name = "VT page draw" };
            mpb = new MaterialPropertyBlock();
            cmd.SetRenderTarget(Texture); cmd.ClearRenderTarget(false, true, Color.black);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
        }

        public Vector2Int RequestTile() { return IdToPos(m_TilePool.First); }

        public bool SetActive(Vector2Int tile) { return m_TilePool.SetActive(PosToId(tile)); }

        public void UpdateTile(Vector2Int tile, LoadRequest request)
        {
            if (!SetActive(tile))
                return;
            DrawPage(request, new RectInt(tile.x * TileSizeWithPadding, tile.y * TileSizeWithPadding, TileSizeWithPadding, TileSizeWithPadding));
            OnTileUpdateComplete?.Invoke(tile);
        }

        private Vector2Int IdToPos(int id) { return new Vector2Int(id % RegionSize.x, id / RegionSize.x); }
        private int PosToId(Vector2Int tile) { return tile.y * RegionSize.x + tile.x; }

        // the page "load": render this page of the procedural virtual texture into the tile, padding included
        private void DrawPage(LoadRequest r, RectInt rect)
        {
            int table = GetComponent<PageTable>().TableSize;
            int span = 1 << r.MipLevel;                                // finest pages covered by this page
            int px = (r.PageX >> r.MipLevel) << r.MipLevel, py = (r.PageY >> r.MipLevel) << r.MipLevel;
            float spanUV = (float)span / table;
            float texel = spanUV / TileSize;
            // virtual uv at the tile's corner (padding included) and the virtual uv size of the whole padded tile
            mpb.SetVector("_PageRect", new Vector4((float)px / table - PaddingSize * texel, (float)py / table - PaddingSize * texel, TileSizeWithPadding * texel, TileSizeWithPadding * texel));
            mpb.SetVector("_PageInfo", new Vector4(texel, r.MipLevel, 0, 0));
            mpb.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
            cmd.Clear();
            cmd.SetRenderTarget(Texture);
            cmd.SetViewport(new Rect(rect.x, rect.y, rect.width, rect.height));
            cmd.DrawProcedural(Matrix4x4.identity, pageMaterial, 0, MeshTopology.Triangles, 3, 1, mpb);
            Graphics.ExecuteCommandBuffer(cmd);
        }
    }
}
