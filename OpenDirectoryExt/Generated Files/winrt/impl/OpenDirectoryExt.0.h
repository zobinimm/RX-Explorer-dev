// WARNING: Please don't edit this file. It was generated by C++/WinRT v2.0.240111.5

#pragma once
#ifndef WINRT_OpenDirectoryExt_0_H
#define WINRT_OpenDirectoryExt_0_H
WINRT_EXPORT namespace winrt::OpenDirectoryExt
{
    struct IClass;
    struct Class;
}
namespace winrt::impl
{
    template <> struct category<winrt::OpenDirectoryExt::IClass>{ using type = interface_category; };
    template <> struct category<winrt::OpenDirectoryExt::Class>{ using type = class_category; };
    template <> inline constexpr auto& name_v<winrt::OpenDirectoryExt::Class> = L"OpenDirectoryExt.Class";
    template <> inline constexpr auto& name_v<winrt::OpenDirectoryExt::IClass> = L"OpenDirectoryExt.IClass";
    template <> inline constexpr guid guid_v<winrt::OpenDirectoryExt::IClass>{ 0xB7451D5A,0x7E9B,0x5BB5,{ 0x98,0x80,0x04,0xF4,0x39,0xA4,0xF1,0xC3 } }; // B7451D5A-7E9B-5BB5-9880-04F439A4F1C3
    template <> struct default_interface<winrt::OpenDirectoryExt::Class>{ using type = winrt::OpenDirectoryExt::IClass; };
    template <> struct abi<winrt::OpenDirectoryExt::IClass>
    {
        struct WINRT_IMPL_NOVTABLE type : inspectable_abi
        {
        };
    };
    template <typename D>
    struct consume_OpenDirectoryExt_IClass
    {
    };
    template <> struct consume<winrt::OpenDirectoryExt::IClass>
    {
        template <typename D> using type = consume_OpenDirectoryExt_IClass<D>;
    };
}
#endif