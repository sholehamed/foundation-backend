<script setup lang="ts">

interface ClientGenerationExtension {
    group: string;
    downloadUrl: string;
}

const props = defineProps<{
    xClientGeneration?: ClientGenerationExtension;
}>();

console.log(
    '[Scalar.ClientGeneration] props:',
    props
);

function downloadAngular(): void {

    const extension =
        props.xClientGeneration;

    console.log(
        '[Scalar.ClientGeneration] extension:',
        extension
    );

    if (!extension?.downloadUrl) {

        console.error(
            'downloadUrl not found',
            extension
        );

        return;
    }

    window.location.href =
        `${extension.downloadUrl}?target=angular`;
}

</script>

<template>
    <div style="margin-top: 12px;">
        <button
            type="button"
            @click="downloadAngular"
            style="
                display: inline-flex;
                align-items: center;
                gap: 7px;
                min-height: 34px;
                padding: 6px 12px;
                border-radius: 6px;
                border: 1px solid var(--scalar-border-color);
                background: var(--scalar-background-2);
                color: var(--scalar-color-1);
                font-family: inherit;
                font-size: 13px;
                font-weight: 500;
                cursor: pointer;
            "
        >
            ↓ Download Angular Client
        </button>
    </div>
</template>