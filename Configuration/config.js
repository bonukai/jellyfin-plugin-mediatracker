const MediaTrackerConfigPage = {
    pluginUniqueId: 'c4772eae-799e-490d-abff-4de21f99c95e',
};

export default function (view) {
    view.addEventListener('pageshow', async function () {
        const users = await ApiClient.getUsers();

        for (const user of users) {
            const optionElement = document.createElement('option');
            optionElement.value = user.Id;
            optionElement.text = user.Name;

            view.querySelector('#selectUser').options.add(optionElement);
        }

        await updatePage(view);

        view.querySelector('#selectUser').addEventListener(
            'change',
            function () {
                updatePage(view);
            }
        );
    });

    view.querySelector('#configForm').addEventListener(
        'submit',
        async function (e) {
            e.preventDefault();

            Dashboard.showLoadingMsg();

            const config = await ApiClient.getPluginConfiguration(
                MediaTrackerConfigPage.pluginUniqueId
            );

            config.mediaTrackerUrl =
                view.querySelector('#mediaTrackerUrl').value;

            const selectedUserId = view
                .querySelector('#selectUser')
                .selectedOptions.item(0)?.value;

            if (selectedUserId) {
                const updatedUser = {
                    id: selectedUserId,
                    apiToken: view.querySelector('#apiToken').value,
                };

                if (config.users?.find((user) => user.id === selectedUserId)) {
                    config.users = config.users.map((user) =>
                        user.id === selectedUserId ? updatedUser : user
                    );
                } else {
                    config.users = [...config.users, updatedUser];
                }
            }

            const result = await ApiClient.updatePluginConfiguration(
                MediaTrackerConfigPage.pluginUniqueId,
                config
            );

            Dashboard.processPluginConfigurationUpdateResult(result);
        }
    );
}

async function updatePage(view) {
    Dashboard.showLoadingMsg();

    const config = await ApiClient.getPluginConfiguration(
        MediaTrackerConfigPage.pluginUniqueId
    );

    const userIdToApiTokenMap = config.users?.reduce(
        (res, user) => res.set(user.id, user.apiToken),
        new Map()
    );

    const selectedUserId = view.querySelector('#selectUser').value;

    view.querySelector('#apiToken').value =
        userIdToApiTokenMap.get(selectedUserId) || '';

    view.querySelector('#mediaTrackerUrl').value = config.mediaTrackerUrl;

    Dashboard.hideLoadingMsg();
}
