"use strict";

(function ($) {
    "use strict";

    const fullHeight = function () {
        $(".js-fullheight").css("height", $(window).height());
        $(window).resize(function () {
            $(".js-fullheight").css("height", $(window).height());
        });
    };
    fullHeight();

    $(".sidebar-collapse").on("click", function () {
        $("#sidebar").toggleClass("active");
    });

    $("body").on("click", ".page-link", function (e) {
        e.preventDefault();
        $("#page").val($(this).attr("data-val"));
        fetchData();
    });

    $("#logCount").on("change", function () {
        $("#page").val("1");
        fetchData();
    });

    $("#logFilter").on("change", function () {
        $("#page").val("1");
        fetchData();
    });

    $("#submit").on("click", function () {
        $("#page").val("1");
        fetchData();
    });

    $("body").on("click", ".modal-trigger", function (e) {
        e.preventDefault();

        const modal = $("#messageModal");
        const modalBody = modal.find(".modal-body");
        const dataType = $(this).attr("data-type");
        let message = $(this).find("span").text();

        if (dataType === "xml") {
            message = $(this).find("span").html();
            message = formatXml(message, "  ");
            $(modalBody).removeClass("wrapped");
        } else if (dataType === "json") {
            const prop = JSON.parse(message);
            message = JSON.stringify(prop, null, 2);
            $(modalBody).removeClass("wrapped");
        } else {
            $(modalBody).addClass("wrapped");
        }

        modalBody.find("pre").text(message);
        modal.modal("show");
        $('.stacktrace').netStack({
            prettyprint: true
        });
    });

    $("#saveJwt").on("click", function () {
        const isJwtSaved = $(this).data("saved");
        if (isJwtSaved.toString() === "false") {
            const token = $("#jwtToken").val();
            if (!token) return;

            sessionStorage.setItem("serilogui_token", token);
            $("#jwtToken").remove();
            $("#tokenContainer").text("*********");
            $(this).text("Clear");
            $(this).data("saved", "true");
            $("#jwtModalBtn").find("i").removeClass("fa-unlock").addClass("fa-lock");
            fetchData();
            return;
        }

        sessionStorage.removeItem("serilogui_token");
        $(this).text("Save");
        $(this).data("saved", "false");
        $("#tokenContainer").html('<input type="text" class="form-control" id="jwtToken" autocomplete="off" placeholder="Bearer eyJhbGciOiJSUz...">');
        $("#jwtModalBtn").find("i").removeClass("fa-lock").addClass("fa-unlock");
    });
})(jQuery);

let authType;

const routePrefix = {
    url: "",
    set setUrl(route) {
        this.url = route;
    }
}

const init = (config) => {
    if (config.authType === "Jwt") {
        initTokenUi();
    } else {
        $("#jwtModalBtn").remove();
        sessionStorage.removeItem("serilogui_token");
    }

    authType = config.authType;

    routePrefix.setUrl = config.routePrefix;
    fetchData();
}

const fetchData = () => {
    const tbody = $("#logTable tbody");
    const page = $("#page").val();
    const count = $("#count").children("option:selected").val();
    const level = $("#level").children("option:selected").val();
    const startDate = $("#startDate").val();
    const endDate = $("#endDate").val();

    if (startDate && endDate !== null) {
        const start = Date.parse(startDate);
        const end = Date.parse(endDate);
        if (start > end) {
            alert("Start date cannot be greater than end date");
            return;
        }
    }

    const searchTerm = escape($("#search").val());
    const url = `${location.pathname.replace("/index.html", "")}/api/logs?page=${page}&count=${count}&level=${level}&search=${searchTerm}&startDate=${startDate}&endDate=${endDate}`;
    const token = sessionStorage.getItem("serilogui_token");
    let xf = null;
    if (authType !== "Windows")
        $.ajaxSetup({ headers: { 'Authorization': token } });
    else {
        xf = {
            withCredentials: true
        };
    }
    $.get({
        url: url,
        xhrFields: xf,
        success: function (data) {
            $("#totalLogs").html(data.total);
            $("#showingItemsStart").html(data.page);
            $("#showingItemsEnd").html(data.count);
            $(tbody).empty();
            data.logs.forEach(function (log) {
                let exception = "";
                if (log.exception != undefined) {
                    exception =
                        `<a href="#" title="Click to view" class="modal-trigger" data-type="text">
                        View <span style="display: none">${log.exception}</span>
                    </a>`;
                }
                const row = `<tr class="${log.level}">
                <td class="text-center">${log.rowNo}</td>
                <td class="text-center"><span class="log-level text-white ${levelClass(log.level)}">${log.level}</span></td>
                <td class="text-center">${formatDate(log.timestamp)}</td>
                <td class="log-message">
                    <span class="overflow-auto"><truncate length="100">${truncateString(log.message, 100)}</truncate></span>
                </td>
                <td class="text-center">
                    ${exception}
                 </td>
                <td class="text-center">
                    <a href="#" class="modal-trigger" title="Click to view" data-type="${log.propertyType}">
                    View
                        <span style="display: none">${log.properties}</span>
                    </a>
                </td>
            </tr>`;
                $(tbody).append(row);
            });
            paging(data.total, data.count, data.currentPage);
        }
    }).fail(function (error) {
        if (error.status === 403) {
            console.log(error);
            alert("You are not authorized you to access logs.\r\nYou are not logged in or you don't have enough permissions to perform the requested operation.");
        } else if (error.status === 500) {
            const x = JSON.parse(error.responseJSON.errorMessage);
            alert(x.errorMessage);
        } else {
            alert(error.responseText);
        }
    });
}

const levelClass = (logLevel) => {
    switch (logLevel) {
        case "Verbose":
        case "Debug":
            return "bg-success";
        case "Information":
            return "bg-primary";
        case "Warning":
            return "bg-warning";
        case "Error":
            return "bg-danger";
        default:
            return "";
    }
}

const formatDate = (date) => {
    var dt = new Date(date);
    return `${(dt.getMonth() + 1).toString().padStart(2, "0")}/${dt.getDate().toString().padStart(2, "0")}/${dt.getFullYear().toString().padStart(4, "0")}
            ${dt.getHours().toString().padStart(2, "0")}:${dt.getMinutes().toString().padStart(2, "0")}:${dt.getSeconds().toString().padStart(2, "0")}.${dt.getMilliseconds().toString()}`;
}

const truncateString = (str, num) => {
    if (str.length <= num) {
        return str;
    }

    const truncated = str.slice(0, num) + "...";
    const html = `<a href="#" title="Click to view" class="modal-trigger" data-type="text">
                    ${truncated}
                    <span style=\"display: none\">${str}</span>
                  </a>`;

    return html;
}

const formatXml = (xml, tab) => { // tab = optional indent value, default is tab (\t)
    var formatted = "", indent = "";
    tab = tab || "\t";
    xml.split(/>\s*</).forEach(function (node) {
        if (node.match(/^\/\w/)) indent = indent.substring(tab.length); // decrease indent by one "tab"
        formatted += indent + "<" + node + ">\r\n";
        if (node.match(/^<?\w[^>]*[^\/]$/)) indent += tab;              // increase indent
    });
    return formatted.substring(1, formatted.length - 3);
}

const paging = (totalItems, itemPerPage, currentPage) => {
    const defaultPageLength = 5;
    const totalPages = Math.ceil(totalItems / itemPerPage);
    //let totalPages = parseInt(totalItems / itemPerPage);
    //totalPages += totalItems % itemPerPage !== 0 ? 1 : 0;
    let startIndex, endIndex;

    if (totalPages <= defaultPageLength) {
        startIndex = 1;
        endIndex = totalPages;
    } else if (totalPages === currentPage) {
        startIndex = totalPages - defaultPageLength;
        endIndex = totalPages;
    } else {
        startIndex = currentPage;
        endIndex = (currentPage - 1) + defaultPageLength;
    }

    const hasPrev = totalPages > 1 && startIndex > 1;
    const hasNext = totalPages > defaultPageLength && endIndex !== totalPages;
    const pagination = $("#pagination");
    $(pagination).empty();

    if (hasPrev) {
        const prevVal = currentPage - 1;
        $(pagination).append(`<li class="page-item first"><a href="#" data-val="1" tabindex="${prevVal}" class="page-link">First</a></li>`);
        $(pagination).append(`<li class="page-item previous"><a href="#" data-val="${prevVal}" tabindex="${prevVal}" class="page-link">Previous</a></li>`);
    }

    for (let i = startIndex; i <= endIndex; i++) {
        if (currentPage === i) {
            $(pagination).append(`<li class="page-item active"><span data-val="${i}" class="page-link disabled">${i} <span class="sr-only">(current)</span></span></li>`);
        }
        else {
            $(pagination).append(`<li><a href="#" data-val="${i}" tabindex="${i}" class="page-link">${i}</a></li>`);
        }
    }

    if (hasNext) {
        const nextVal = currentPage + 1;
        $(pagination).append(`<li class="page-item ">&nbsp;...&nbsp;</li>`);
        $(pagination).append(`<li class="page-item next"><a href="#" data-val="${nextVal}" tabindex="${nextVal}" class="page-link">Next</a></li>`);
        $(pagination).append(`<li class="page-item previous"><a href="#" data-val="${totalPages}" tabindex="${nextVal}" class="page-link">Last</a></li>`);
    }
}

const initTokenUi = () => {
    const token = sessionStorage.getItem("serilogui_token");
    if (!token) return;

    $("#jwtToken").remove();
    $("#tokenContainer").text("*********");
    $("#saveJwt").text("Clear").data("saved", "true");
    $("#jwtModalBtn").find("i").removeClass("fa-unlock").addClass("fa-lock");
}