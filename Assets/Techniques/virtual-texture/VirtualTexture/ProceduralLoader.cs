using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualTexture
{
    // Stands in for the source's FileLoader. Same request queue (duplicates ignored, coarsest mip served first),
    // but a "load" is a draw of the procedural page, and at most pagesPerFrame pages are served per frame
    // (the source loads from files with a thread limit).
    public class ProceduralLoader : MonoBehaviour
    {
        public event Action<LoadRequest> OnLoadComplete;
        public int pagesPerFrame = 4;
        private readonly List<LoadRequest> m_PendingRequests = new List<LoadRequest>();
        public int Pending => m_PendingRequests.Count;

        private void Update()
        {
            for (int n = 0; n < pagesPerFrame && m_PendingRequests.Count > 0; n++)
            {
                // highest mip level first (same order as the source)
                m_PendingRequests.Sort((x, y) => x.MipLevel.CompareTo(y.MipLevel));
                var req = m_PendingRequests[m_PendingRequests.Count - 1];
                m_PendingRequests.RemoveAt(m_PendingRequests.Count - 1);
                OnLoadComplete?.Invoke(req);
            }
        }

        public LoadRequest Request(int x, int y, int mip)
        {
            foreach (var r in m_PendingRequests)
                if (r.PageX == x && r.PageY == y && r.MipLevel == mip)
                    return null;
            var request = new LoadRequest(x, y, mip);
            m_PendingRequests.Add(request);
            return request;
        }
    }
}
