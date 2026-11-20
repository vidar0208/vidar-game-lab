using System.Collections.Generic;
using UnityEngine;

namespace VirtualTexture
{
    // Port of the source's PageTable (logic unchanged): a quadtree of pages per mip, a lookup texture
    // (one texel per finest page: tile x, tile y, mip, frame) and the feedback handling.
    // GameTech changes: concrete component references instead of interfaces, inputs written through VTBinding,
    // a few counters for the labels, the debug texture code left out.
    public class PageTable : MonoBehaviour
    {
        [SerializeField] private int m_TableSize = 64;
        [SerializeField] private int m_MipLevelLimit = 6;

        private TableNode m_PageTable;
        private Dictionary<Vector2Int, TableNode> m_ActivePages = new Dictionary<Vector2Int, TableNode>();
        private Texture2D m_LookupTexture;
        private ProceduralLoader m_Loader;
        private TiledTexture m_TileTexture;

        public int TableSize { get { return m_TableSize; } }
        public int MaxMipLevel { get { return Mathf.Min(m_MipLevelLimit, (int)Mathf.Log(TableSize, 2)); } }
        public Texture2D LookupTexture => m_LookupTexture;

        // counters (GameTech)
        public int LastRequestedPages { get; private set; }   // distinct pages (x >> mip, y >> mip, mip) in the last feedback
        public int ResidentPages => m_ActivePages.Count;
        public int TotalLoads { get; private set; }
        readonly HashSet<int> requested = new HashSet<int>();

        private void Start()
        {
            m_PageTable = new TableNode(MaxMipLevel, 0, 0, TableSize, TableSize);
            m_LookupTexture = new Texture2D(TableSize, TableSize, TextureFormat.RGBA32, false, true);
            m_LookupTexture.filterMode = FilterMode.Point;
            m_LookupTexture.wrapMode = TextureWrapMode.Clamp;

            var binding = GetComponent<VTBinding>();
            binding.SetTexture("_VTLookupTex", m_LookupTexture);
            binding.SetVector("_VTPageParam", new Vector4(TableSize, 1.0f / TableSize, MaxMipLevel, 0));

            m_Loader = GetComponent<ProceduralLoader>();
            m_Loader.OnLoadComplete += OnLoadComplete;
            m_TileTexture = GetComponent<TiledTexture>();
            m_TileTexture.OnTileUpdateComplete += InvalidatePage;
            GetComponent<FeedbackReader>().OnFeedbackReadComplete += ProcessFeedback;
        }

        // feedback read back: activate the requested pages, then write the active ones into the lookup texture
        private void ProcessFeedback(Texture2D texture)
        {
            requested.Clear();
            foreach (var c in texture.GetRawTextureData<Color32>())
            {
                if (c.b <= MaxMipLevel) requested.Add((c.b << 16) | ((c.g >> c.b) << 8) | (c.r >> c.b));
                ActivatePage(c.r, c.g, c.b);
            }
            LastRequestedPages = requested.Count;

            var currentFrame = (byte)Time.frameCount;
            var pixels = m_LookupTexture.GetRawTextureData<Color32>();
            foreach (var kv in m_ActivePages)
            {
                var page = kv.Value;
                if (page.Payload.ActiveFrame != Time.frameCount)
                    continue;
                // a = frame number, so stale texels from earlier frames get overwritten
                var c = new Color32((byte)page.Payload.TileIndex.x, (byte)page.Payload.TileIndex.y, (byte)page.MipLevel, currentFrame);
                for (int y = page.Rect.y; y < page.Rect.yMax; y++)
                {
                    for (int x = page.Rect.x; x < page.Rect.xMax; x++)
                    {
                        var id = y * TableSize + x;
                        if (pixels[id].b > c.b ||                 // keep the finest mip
                            pixels[id].a != currentFrame)         // not written this frame yet
                            pixels[id] = c;
                    }
                }
            }
            m_LookupTexture.Apply(false);
        }

        private TableNode ActivatePage(int x, int y, int mip)
        {
            if (mip > m_PageTable.MipLevel)
                return null;
            var page = m_PageTable.GetAvailable(x, y, mip);
            if (page == null)
            {
                LoadPage(x, y, m_PageTable);      // nothing resident: load the root
                return null;
            }
            else if (page.MipLevel > mip)
            {
                LoadPage(x, y, page.GetChild(x, y));   // coarser page resident: ask for the next finer one
            }
            m_TileTexture.SetActive(page.Payload.TileIndex);
            page.Payload.ActiveFrame = Time.frameCount;
            return page;
        }

        private void LoadPage(int x, int y, TableNode node)
        {
            if (node == null)
                return;
            if (node.Payload.LoadRequest != null)
                return;
            node.Payload.LoadRequest = m_Loader.Request(x, y, node.MipLevel);
        }

        private void OnLoadComplete(LoadRequest request)
        {
            var node = m_PageTable.Get(request.PageX, request.PageY, request.MipLevel);
            if (node == null || node.Payload.LoadRequest != request)
                return;
            node.Payload.LoadRequest = null;
            var id = m_TileTexture.RequestTile();
            m_TileTexture.UpdateTile(id, request);
            node.Payload.TileIndex = id;
            m_ActivePages[id] = node;
            TotalLoads++;
        }

        private void InvalidatePage(Vector2Int id)
        {
            if (!m_ActivePages.TryGetValue(id, out var node))
                return;
            node.Payload.ResetTileIndex();
            m_ActivePages.Remove(id);
        }
    }
}
