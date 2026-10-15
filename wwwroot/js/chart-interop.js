window.chartInterop = {
    myChart: null,

    renderScoreHistoryChart: function (canvasId, labels, scores, thresholds) {
        const canvas = document.getElementById(canvasId);
        if (!canvas || !window.Chart) return;

        if (this.myChart) {
            this.myChart.destroy();
            this.myChart = null;
        }

        const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
        const gridColor = isDark ? 'rgba(255, 255, 255, 0.08)' : 'rgba(0, 0, 0, 0.06)';
        const textColor = isDark ? '#94a3b8' : '#64748b';

        const ctx = canvas.getContext('2d');
        this.myChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [
                    {
                        label: 'Keystroke Confidence Score (%)',
                        data: scores,
                        borderColor: '#0d9488',
                        backgroundColor: 'rgba(13, 148, 136, 0.12)',
                        borderWidth: 2,
                        pointRadius: 4,
                        pointBackgroundColor: '#0d9488',
                        fill: true,
                        tension: 0.25
                    },
                    {
                        label: 'Adaptive Threshold Cutoff (%)',
                        data: thresholds,
                        borderColor: '#f59e0b',
                        borderDash: [5, 4],
                        borderWidth: 2,
                        pointRadius: 2,
                        pointBackgroundColor: '#f59e0b',
                        fill: false
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        labels: {
                            color: textColor,
                            font: { family: "'IBM Plex Sans', sans-serif" }
                        }
                    },
                    tooltip: {
                        backgroundColor: isDark ? '#1e293b' : '#ffffff',
                        titleColor: isDark ? '#f8fafc' : '#0f172a',
                        bodyColor: isDark ? '#cbd5e1' : '#334155',
                        borderColor: isDark ? '#334155' : '#e2e8f0',
                        borderWidth: 1
                    }
                },
                scales: {
                    x: {
                        grid: { color: gridColor },
                        ticks: { color: textColor }
                    },
                    y: {
                        beginAtZero: true,
                        max: 100,
                        grid: { color: gridColor },
                        ticks: {
                            color: textColor,
                            callback: function(value) { return value + '%'; }
                        }
                    }
                }
            }
        });
    }
};
