window.quillInterop = {
    editors: {},

    init: function (containerId, initialContent, dotNetRef) {
        const container = document.getElementById(containerId);
        if (!container || !window.Quill) return;

        // Word-like font families.
        const Font = Quill.import('formats/font');
        Font.whitelist = [
            'arial',
            'calibri',
            'times-new-roman',
            'georgia',
            'verdana',
            'aptos'
        ];
        Quill.register(Font, true);

        // Word-like numeric font sizes.
        const Size = Quill.import('attributors/style/size');
        Size.whitelist = [
            '10px',
            '11px',
            '12px',
            '14px',
            '16px',
            '18px',
            '20px',
            '24px',
            '28px',
            '32px',
            '36px',
            '48px'
        ];
        Quill.register(Size, true);

        const quill = new Quill('#' + containerId, {
            theme: 'snow',
            placeholder: 'Start writing your document...',
            modules: {
                history: {
                    delay: 800,
                    maxStack: 100,
                    userOnly: true
                },
                toolbar: [
                    [
                        {
                            font: [
                                'arial',
                                'calibri',
                                'times-new-roman',
                                'georgia',
                                'verdana',
                                'aptos'
                            ]
                        },
                        {
                            size: [
                                '10px',
                                '11px',
                                '12px',
                                '14px',
                                '16px',
                                '18px',
                                '20px',
                                '24px',
                                '28px',
                                '32px',
                                '36px',
                                '48px'
                            ]
                        }
                    ],
                    [
                        'bold',
                        'italic',
                        'underline',
                        'strike'
                    ],
                    [
                        { color: [] },
                        { background: [] }
                    ],
                    [
                        { header: [1, 2, 3, 4, 5, 6, false] }
                    ],
                    [
                        { align: [] }
                    ],
                    [
                        { list: 'ordered' },
                        { list: 'bullet' },
                        { indent: '-1' },
                        { indent: '+1' }
                    ],
                    [
                        { script: 'sub' },
                        { script: 'super' }
                    ],
                    [
                        'blockquote',
                        'code-block',
                        'link'
                    ],
                    [
                        'undo',
                        'redo',
                        'clean'
                    ]
                ]
            }
        });

        if (initialContent) {
            quill.root.innerHTML = initialContent;
        }

        quill.on('text-change', function () {
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync(
                    'OnContentChanged',
                    quill.root.innerHTML
                );
            }
        });

        const toolbar = quill.getModule('toolbar');

        toolbar.addHandler('undo', function () {
            quill.history.undo();
        });

        toolbar.addHandler('redo', function () {
            quill.history.redo();
        });

        this.editors[containerId] = quill;
    },

    getContent: function (containerId) {
        if (this.editors[containerId]) {
            return this.editors[containerId].root.innerHTML;
        }

        return '';
    },

    setContent: function (containerId, content) {
        if (this.editors[containerId]) {
            this.editors[containerId].root.innerHTML = content || '';
        }
    },

    destroy: function (containerId) {
        delete this.editors[containerId];
    }
};

window.fileDownloadInterop = {
    downloadFileFromBytes: function (fileName, contentType, base64Data) {
        const link = document.createElement('a');

        link.download = fileName;

        link.href =
            `data:${contentType};base64,${base64Data}`;

        document.body.appendChild(link);

        link.click();

        document.body.removeChild(link);
    }
};
