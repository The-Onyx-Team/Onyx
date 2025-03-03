window.postRedirect = (url, data) => {
    let form = document.createElement("form");
    form.method = "POST";
    form.action = url;

    for (let key in data) {
        if (data.hasOwnProperty(key)) {
            let input = document.createElement("input");
            input.type = "hidden";
            input.name = key;
            input.value = data[key];
            form.appendChild(input);
        }
    }

    document.body.appendChild(form);
    form.submit();
};
