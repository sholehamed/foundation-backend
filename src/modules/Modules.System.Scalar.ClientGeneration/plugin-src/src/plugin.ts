import ClientGenerationButton
    from './ClientGenerationButton.vue';

export default () => ({
    name: 'scalar-client-generation',

    extensions: [
        {
            name: 'x-client-generation',

            component:
                ClientGenerationButton
        }
    ]
});