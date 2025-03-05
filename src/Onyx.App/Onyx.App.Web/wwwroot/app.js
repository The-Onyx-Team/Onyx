window.postRedirect = (url, data) => {
    let form = document.createElement("form");
    form.method = "POST";
    form.action = url;
    form.id = "dynamic-post-form-" + new Date().getTime();
    form.setAttribute("name", "dynamicPostForm");

    if (!data.FormName) {
        data.FormName = form.id;
    }

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
    
    setTimeout(() => {
        document.body.removeChild(form);
    }, 0);
};
