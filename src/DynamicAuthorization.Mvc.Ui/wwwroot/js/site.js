$(function () {
    $(".delete-item").click(function (e) {
        confirm("Are you sure want delete this role ?");
        e.preventDefault();
        const anchor = $(this);
        const url = $(anchor).attr("href");
        $.ajax({
            contentType: "application/json; charset=utf-8",
            dataType: "json",
            processData: false,
            type: "DELETE",
            url: url,
            success: function () {
                $(anchor).parent("td").parent("tr").fadeOut("slow",
                    function () {
                        $(this).remove();
                    });
            },
            error: function (xmlHttpRequest, textStatus, errorThrown) {
                let message = `${textStatus} ${xmlHttpRequest.status} ${errorThrown}`;
                if (xmlHttpRequest.responseText !== null) {
                    const response = JSON.parse(xmlHttpRequest.responseText);
                    for (let error of response["Error"]) {
                        message += `\n${error}`;
                    }
                }

                alert(message);
            }
        });
    });

    $("#tree").bonsai({
        expandAll: false,
        checkboxes: true,
        createInputs: "checkbox"
    });

    $("form").submit(function () {
        let controllerIndex = 0, actionIndex = 0;
        $('.controller > input[type="checkbox"]:checked, .controller > input[type="checkbox"]:indeterminate').each(function () {
            const controller = $(this);
            if ($(controller).prop("indeterminate")) {
                $(controller).prop("checked", true);
            }
            const controllerName = "SelectedControllers[" + controllerIndex + "]";
            $(controller).prop("name", controllerName + ".Name");

            const area = $(controller).next().next();
            $(area).prop("name", controllerName + ".AreaName");

            $('ul > li > input[type="checkbox"]:checked', $(controller).parent()).each(function () {
                const action = $(this);
                const actionName = controllerName + ".Actions[" + actionIndex + "].Name";
                $(action).prop("name", actionName);
                actionIndex++;
            });
            actionIndex = 0;
            controllerIndex++;
        });

        return true;
    });
});