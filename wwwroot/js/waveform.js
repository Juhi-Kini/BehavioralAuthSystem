window.waveformRenderer = {
    update: function (svgContainerId, holdTimes, latencies) {
        const svg = document.getElementById(svgContainerId);
        if (!svg) return;

        svg.innerHTML = ''; // Clear previous

        if (!holdTimes || holdTimes.length === 0) return;

        const width = svg.clientWidth || 300;
        const height = svg.clientHeight || 60;
        const padding = 10;
        const count = holdTimes.length;
        const step = (width - 2 * padding) / Math.max(count, 1);

        const maxHold = Math.max(...holdTimes, 150);
        const maxLat = latencies.length > 0 ? Math.max(...latencies, 250) : 100;

        // Group element
        const ns = "http://www.w3.org/2000/svg";

        // Draw Hold Time Bars (Teal)
        holdTimes.forEach((hold, idx) => {
            const x = padding + idx * step;
            const barHeight = Math.min((hold / maxHold) * (height - 20), height - 10);
            const y = height - barHeight - 5;

            const rect = document.createElementNS(ns, "rect");
            rect.setAttribute("x", x);
            rect.setAttribute("y", y);
            rect.setAttribute("width", Math.max(step * 0.6, 6));
            rect.setAttribute("height", barHeight);
            rect.setAttribute("rx", "3");
            rect.setAttribute("fill", "url(#holdGradient)");
            rect.setAttribute("opacity", "0.85");
            svg.appendChild(rect);
        });

        // Draw Latency Path Line (Amber)
        if (latencies && latencies.length > 0) {
            let pathD = "";
            latencies.forEach((lat, idx) => {
                const x = padding + (idx + 0.5) * step;
                const y = height - Math.min((lat / maxLat) * (height - 20), height - 10) - 5;

                if (idx === 0) {
                    pathD += `M ${x} ${y}`;
                } else {
                    pathD += ` L ${x} ${y}`;
                }

                // Node circle
                const circle = document.createElementNS(ns, "circle");
                circle.setAttribute("cx", x);
                circle.setAttribute("cy", y);
                circle.setAttribute("r", "3");
                circle.setAttribute("fill", "#f59e0b");
                svg.appendChild(circle);
            });

            if (pathD) {
                const path = document.createElementNS(ns, "path");
                path.setAttribute("d", pathD);
                path.setAttribute("fill", "none");
                path.setAttribute("stroke", "#f59e0b");
                path.setAttribute("stroke-width", "2");
                path.setAttribute("stroke-dasharray", "3,3");
                svg.appendChild(path);
            }
        }

        // Add SVG Gradients
        const defs = document.createElementNS(ns, "defs");
        defs.innerHTML = `
            <linearGradient id="holdGradient" x1="0%" y1="0%" x2="0%" y2="100%">
                <stop offset="0%" stop-color="#0d9488" />
                <stop offset="100%" stop-color="#14b8a6" />
            </linearGradient>
        `;
        svg.prepend(defs);
    },

    clear: function (svgContainerId) {
        const svg = document.getElementById(svgContainerId);
        if (svg) svg.innerHTML = '';
    }
};
