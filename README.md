graph LR
    subgraph Common
        DataService["DBC Data Service"]
        EnvSetup["DBC Environment Setup"]
        FeatureSwitch["DBC Feature Switch"]
        AccDataCore["USTC Accounting Data Core"]
        Foundation["USTC Foundation"]
        ObsCheck["USTC Obsolete Check"]
        Toolbox["USTC Toolbox"]
        UserMgr["USTC User Manager"]
        DataDist["DBC Data Distribution"]
        DimMgr["DBC Dimension Manager"]
        VATCore["USTC VAT Core"]
        Kyriba["USTC Kyriba"]
        Refinitiv["USTC Refinitiv App"]
        TestLibs["DBC Test Libraries"]
        BC2ADLSE["DBC USTC BC2ADLSE"]
    end

    subgraph BHG
        BHGFound["BHG Foundation App"]
        AtoZ["AtoZ Integration"]
        BHGDimBridge["BHG Dimension Manager Bridge"]
        BHGMedius["BHG MediusFlow"]
        BHGSAFT["BHG Standard Audit File"]
        BHGDataDistBridge["DBC BHG Data Distribution Bridge"]
        DataSec["DBC Data Security"]
        BHGPlatform["BHG Platform"]
    end

    subgraph GRM
        GRMFound["GRM Foundation"]
        GRMFinInt["GRM Finance Interest"]
        GRMConcur["GRM Concur"]
        GRMDataAPI["GRM Data API"]
        GRMPayMgmt["GRM Payment Management"]
        GRMReportPack["GRM Report Pack"]
        GRMPlatform["GRM Platform"]
    end

    subgraph SIFO
        SIFOFound["SIFO Foundation App"]
        EditGL["USTC Edit GL"]
        SIFOPlatform["SIFO Platform"]
    end

    %% Common internal deps
    DataDist --> UserMgr
    DimMgr --> FeatureSwitch
    VATCore --> Foundation
    Refinitiv --> Foundation

    %% BHG internal deps
    BHGFound --> FeatureSwitch
    BHGFound --> UserMgr
    AtoZ --> BHGFound
    BHGDimBridge --> FeatureSwitch
    BHGDimBridge --> BHGFound
    BHGDimBridge --> DimMgr
    BHGMedius --> BHGFound
    BHGSAFT --> FeatureSwitch
    BHGSAFT --> TestLibs
    BHGDataDistBridge --> BHGFound
    BHGDataDistBridge --> DataDist
    DataSec --> BHGFound
    BHGPlatform --> UserMgr
    BHGPlatform --> FeatureSwitch
    BHGPlatform --> DimMgr
    BHGPlatform --> TestLibs
    BHGPlatform --> BHGFound
    BHGPlatform --> AtoZ
    BHGPlatform --> BHGSAFT
    BHGPlatform --> Kyriba
    BHGPlatform --> DataDist
    BHGPlatform --> ObsCheck
    BHGPlatform --> EnvSetup
    BHGPlatform --> DataService
    BHGPlatform --> DataSec
    BHGPlatform --> BHGMedius
    BHGPlatform --> BHGDimBridge
    BHGPlatform --> BHGDataDistBridge

    %% GRM internal deps
    GRMConcur --> Foundation
    GRMDataAPI --> AccDataCore
    GRMDataAPI --> Foundation
    GRMDataAPI --> GRMFound
    GRMDataAPI --> GRMFinInt
    GRMPayMgmt --> GRMFound
    GRMReportPack --> VATCore
    GRMReportPack --> GRMFound
    GRMPlatform --> GRMFound
    GRMPlatform --> GRMConcur
    GRMPlatform --> Foundation
    GRMPlatform --> GRMDataAPI
    GRMPlatform --> GRMFinInt
    GRMPlatform --> Refinitiv
    GRMPlatform --> DataDist
    GRMPlatform --> DimMgr
    GRMPlatform --> VATCore
    GRMPlatform --> EnvSetup
    GRMPlatform --> AccDataCore
    GRMPlatform --> FeatureSwitch
    GRMPlatform --> DataService
    GRMPlatform --> GRMPayMgmt
    GRMPlatform --> GRMReportPack
    GRMPlatform --> BC2ADLSE

    %% SIFO internal deps
    SIFOPlatform --> SIFOFound
    SIFOPlatform --> EditGL
